import React, { useState, useEffect } from 'react';
import axios from 'axios';
import { format, addHours, isSameDay, parseISO } from 'date-fns';
import './App.css';

interface Room {
  upn: string;
  name: string;
  capacity: number;
  floor: string;
}

interface Event {
  id: string;
  subject: string;
  start: string;
  end: string;
  organizer: string;
  organizerEmail: string;
  visitorId?: string;
  hasVisitor: boolean;
  isCancelled: boolean;
  attendeeCount: number;
}

interface RoomEventsResponse {
  room: string;
  eventCount: number;
  events: Event[];
  responseTimeMs: number;
  timestamp: string;
  performance: {
    cacheHit: boolean;
    responseTimeMs: number;
    target: string;
    status: string;
  };
}

const API_BASE_URL = 'http://localhost:7071/api/api';

function App() {
  const [rooms, setRooms] = useState<Room[]>([]);
  const [roomEvents, setRoomEvents] = useState<Record<string, Event[]>>({});
  const [loading, setLoading] = useState(true);
  const [selectedDate, setSelectedDate] = useState(new Date());
  const [performanceData, setPerformanceData] = useState<Record<string, any>>({});
  const [refreshCount, setRefreshCount] = useState(0);

  // 時間軸（9:00 - 22:00）
  const timeSlots = Array.from({ length: 14 }, (_, i) => addHours(new Date().setHours(9, 0, 0, 0), i));

  useEffect(() => {
    loadData();
  }, [refreshCount]);

  const loadData = async () => {
    setLoading(true);
    const startTime = Date.now();

    try {
      // 会議室一覧を取得
      const roomsResponse = await axios.get(`${API_BASE_URL}/rooms`);
      const roomsData = roomsResponse.data.rooms;
      setRooms(roomsData);

      // 各会議室のイベントを並列取得
      const eventPromises = roomsData.map(async (room: Room) => {
        try {
          const response = await axios.get(`${API_BASE_URL}/rooms/${encodeURIComponent(room.upn)}/events`);
          const data: RoomEventsResponse = response.data;
          
          setPerformanceData(prev => ({
            ...prev,
            [room.upn]: data.performance
          }));

          return { roomUpn: room.upn, events: data.events };
        } catch (error) {
          console.error(`Failed to load events for room ${room.upn}:`, error);
          return { roomUpn: room.upn, events: [] };
        }
      });

      const eventResults = await Promise.all(eventPromises);
      const eventsMap = eventResults.reduce((acc, { roomUpn, events }) => {
        acc[roomUpn] = events;
        return acc;
      }, {} as Record<string, Event[]>);

      setRoomEvents(eventsMap);

      const totalTime = Date.now() - startTime;
      console.log(`Total data load time: ${totalTime}ms`);

    } catch (error) {
      console.error('Failed to load data:', error);
    } finally {
      setLoading(false);
    }
  };

  const getEventForTimeSlot = (roomUpn: string, timeSlot: Date): Event | null => {
    const events = roomEvents[roomUpn] || [];
    return events.find(event => {
      const eventStart = parseISO(event.start);
      const eventEnd = parseISO(event.end);
      return timeSlot >= eventStart && timeSlot < eventEnd && isSameDay(eventStart, selectedDate);
    }) || null;
  };

  const getPerformanceStatus = () => {
    const allPerf = Object.values(performanceData);
    if (allPerf.length === 0) return null;

    const avgResponseTime = allPerf.reduce((sum: number, perf: any) => sum + perf.responseTimeMs, 0) / allPerf.length;
    const slowCount = allPerf.filter((perf: any) => perf.status === 'SLOW').length;

    return {
      avgResponseTime,
      slowCount,
      totalRequests: allPerf.length,
      status: avgResponseTime <= 10000 ? 'OK' : 'SLOW'
    };
  };

  const perfStatus = getPerformanceStatus();

  if (loading) {
    return (
      <div className="loading-container">
        <div className="loading-spinner"></div>
        <div>会議室データを読み込み中...</div>
      </div>
    );
  }

  return (
    <div className="App">
      <header className="app-header">
        <h1>🏢 ResourceLook - 会議室予約状況</h1>
        <div className="header-controls">
          <input
            type="date"
            value={format(selectedDate, 'yyyy-MM-dd')}
            onChange={(e) => setSelectedDate(parseISO(e.target.value))}
            className="date-picker"
          />
          <button onClick={() => setRefreshCount(c => c + 1)} className="refresh-btn">
            🔄 更新
          </button>
        </div>
      </header>

      {perfStatus && (
        <div className={`performance-bar ${perfStatus.status.toLowerCase()}`}>
          <span>📊 レスポンス性能: 平均{Math.round(perfStatus.avgResponseTime)}ms</span>
          <span>🎯 目標: P95 ≤ 10秒</span>
          <span>📈 ステータス: {perfStatus.status}</span>
          {perfStatus.slowCount > 0 && (
            <span className="warning">⚠️ {perfStatus.slowCount}件のスロークエリ</span>
          )}
        </div>
      )}

      <div className="calendar-container">
        <div className="time-column">
          <div className="time-header">時間</div>
          {timeSlots.map(time => (
            <div key={time.toString()} className="time-slot">
              {format(time, 'HH:mm')}
            </div>
          ))}
        </div>

        {rooms.map(room => (
          <div key={room.upn} className="room-column">
            <div className="room-header">
              <div className="room-name">{room.name}</div>
              <div className="room-details">
                <span className="capacity">👥 {room.capacity}名</span>
                <span className="floor">🏢 {room.floor}</span>
              </div>
              {performanceData[room.upn] && (
                <div className={`perf-indicator ${performanceData[room.upn].status.toLowerCase()}`}>
                  {Math.round(performanceData[room.upn].responseTimeMs)}ms
                </div>
              )}
            </div>
            
            {timeSlots.map(timeSlot => {
              const event = getEventForTimeSlot(room.upn, timeSlot);
              return (
                <div key={timeSlot.toString()} className="time-slot">
                  {event ? (
                    <div className={`event ${event.hasVisitor ? 'visitor-event' : 'normal-event'} ${event.isCancelled ? 'cancelled' : ''}`}>
                      <div className="event-subject">{event.subject}</div>
                      <div className="event-organizer">{event.organizer}</div>
                      {event.hasVisitor && (
                        <div className="visitor-badge">
                          👤 来客: {event.visitorId?.substring(0, 8)}...
                        </div>
                      )}
                      <div className="event-time">
                        {format(parseISO(event.start), 'HH:mm')} - {format(parseISO(event.end), 'HH:mm')}
                      </div>
                    </div>
                  ) : (
                    <div className="empty-slot">空き</div>
                  )}
                </div>
              );
            })}
          </div>
        ))}
      </div>

      <footer className="app-footer">
        <div>📅 {format(selectedDate, 'yyyy年MM月dd日 (E)', { locale: undefined })}</div>
        <div>🔄 最終更新: {new Date().toLocaleTimeString()}</div>
        <div>⚡ PoC: Graph API Change Notifications + Delta Queries</div>
      </footer>
    </div>
  );
}

export default App;
