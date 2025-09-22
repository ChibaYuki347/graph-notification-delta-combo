import React, { useState, useEffect } from 'react';
import axios from 'axios';
import { format, startOfWeek, addDays, isToday, startOfHour, addHours } from 'date-fns';
import { ja } from 'date-fns/locale';
import './App.css';

interface Event {
  id: string;
  subject: string;
  start: {
    dateTime: string;
    timeZone: string;
  };
  end: {
    dateTime: string;
    timeZone: string;
  };
  room?: string; // 追加: どの会議室のイベントか
  organizer: {
    emailAddress: {
      name: string;
      address: string;
    };
  };
  created?: string;
  ingestedAtUtc?: string;
  attendees?: Array<{
    emailAddress: {
      name: string;
      address: string;
    };
    status: {
      response: string;
      time: string;
    };
  }>;
  isVisitorMeeting?: boolean;
  visitorInfo?: {
    hasVisitor: boolean;
    extractedNames: string[];
    confidence: number;
  };
}

interface PerformanceStats {
  responseTime: number;
  requestCount: number;
  averageResponseTime: number;
  p95ResponseTime: number;
  lastApiResponseTime?: number; // 新規追加: API側レスポンス時間
  totalEvents?: number; // 新規追加: 取得イベント総数
}

// API ベースURL (環境変数 or デフォルト)
const API_BASE = (process.env.REACT_APP_FUNCTION_BASE_URL || 'http://localhost:7071');

function App() {
  const [events, setEvents] = useState<Event[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [selectedDate, setSelectedDate] = useState(new Date());
  const [viewMode, setViewMode] = useState<'day' | 'week'>('week');
  const [performanceStats, setPerformanceStats] = useState<PerformanceStats>({
    responseTime: 0,
    requestCount: 0,
    averageResponseTime: 0,
    p95ResponseTime: 0
  });
  // バルク作成用フォーム状態
  const [bulkCount, setBulkCount] = useState(5);
  const [bulkVisitors, setBulkVisitors] = useState(true);
  const [bulkSkipCache, setBulkSkipCache] = useState(false);
  const [bulkLoading, setBulkLoading] = useState(false);
  const [bulkMessage, setBulkMessage] = useState<string | null>(null);
  // バルク作成対象の会議室（表示用選択と独立）
  const [bulkRooms, setBulkRooms] = useState<string[]>([]);

  // 選択可能な会議室一覧 (16室)
  const allRooms = React.useMemo(() => Array.from({length:16}, (_,i)=>`ConfRoom${i+1}@bbslooklab.onmicrosoft.com`), []);
  const [selectedRooms, setSelectedRooms] = useState<string[]>([allRooms[0]]);

  // 部屋ごとのカラー (循環)
  const colors = React.useMemo(()=>[
    '#0984e3','#6c5ce7','#00b894','#e17055','#fd79a8',
    '#00cec9','#e84393','#2d3436','#74b9ff','#a29bfe',
    '#55efc4','#fab1a0','#ff7675','#ffeaa7','#81ecec','#b2bec3'
  ], []);
  const colorMap = React.useMemo(() => new Map(allRooms.map((r,i)=>[r, colors[i % colors.length]])), [allRooms, colors]);
  // ルーム検索用
  const [roomFilter, setRoomFilter] = useState("");

  // 新しい統合API呼び出し
  const fetchAllRoomEvents = async (): Promise<{ events: Event[], apiStats?: any }> => {
    const response = await axios.get(`${API_BASE}/api/room-events?raw=true`);
    const data = response.data;
    
    // 新しいAPI形式: { events: [], eventCount: number, stats: {...}, performance: {...} }
    const sourceArray: any[] = Array.isArray(data?.events) ? data.events : [];
    console.log(`API Response: ${sourceArray.length} events from unified endpoint, API response time: ${data?.performance?.responseTimeMs}ms`);
    
    const mapped: Event[] = sourceArray.map((e: any) => {
      // 新しいAPIレスポンス形式に対応
      const startIso = e.start || e.startDateTime || e.Start || e.startISO;
      const endIso = e.end || e.endDateTime || e.End || e.endISO;
      const organizerName = e.organizer || e.organizerName || 'Unknown';
      const organizerEmail = e.organizerEmail || 'unknown@example.com';
      
      return {
        id: e.id || e.eventId || crypto.randomUUID(),
        subject: e.subject || '(no subject)',
        start: { dateTime: startIso, timeZone: 'Asia/Tokyo' },
        end: { dateTime: endIso, timeZone: 'Asia/Tokyo' },
        organizer: { emailAddress: { name: organizerName, address: organizerEmail } },
        created: e.created || e.Created || null,
        ingestedAtUtc: e.ingestedAtUtc || null,
        attendees: e.attendees || [],
        isVisitorMeeting: !!e.hasVisitor || !!e.visitorId,
        visitorInfo: e.visitorId ? { hasVisitor: true, extractedNames: [e.visitorId], confidence: 1 } : undefined,
        room: e.room || e.roomName || 'Unknown Room'
      };
    }).filter(ev => ev.start?.dateTime && ev.end?.dateTime);
    
    return { 
      events: mapped, 
      apiStats: {
        responseTimeMs: data?.performance?.responseTimeMs,
        totalEvents: data?.eventCount,
        stats: data?.stats
      }
    };
  };

  const lastFetchCompletedAtRef = React.useRef<number | null>(null);
  const fetchEvents = React.useCallback(async () => {
    const startTime = Date.now();
    setLoading(true);
    setError(null);

    try {
      // 統合APIエンドポイントを使用 (全会議室のイベントを一括取得)
      const result = await fetchAllRoomEvents();
      const allEvents = result.events;
      const apiStats = result.apiStats;
      
      // 選択された会議室のイベントのみフィルタリング
      const filteredEvents = allEvents.filter(event => 
        selectedRooms.some(roomUpn => event.room === roomUpn)
      );
      
      console.log(`Total events: ${allEvents.length}, Filtered for selected rooms: ${filteredEvents.length}`);
      setEvents(filteredEvents);

      const responseTime = Date.now() - startTime;
      lastFetchCompletedAtRef.current = Date.now();

      // パフォーマンス統計更新（API側統計も含む）
      setPerformanceStats(prev => {
        const newRequestCount = prev.requestCount + 1;
        const newAverageResponseTime = (prev.averageResponseTime * prev.requestCount + responseTime) / newRequestCount;
        const newP95ResponseTime = Math.max(prev.p95ResponseTime, responseTime);
        return {
          responseTime,
          requestCount: newRequestCount,
          averageResponseTime: newAverageResponseTime,
          p95ResponseTime: newP95ResponseTime,
          lastApiResponseTime: apiStats?.responseTimeMs,
          totalEvents: apiStats?.totalEvents
        };
      });
    } catch (err) {
      console.error('API Error:', err);
      setError(err instanceof Error ? err.message : 'エラーが発生しました');
      setEvents([]);
    } finally {
      setLoading(false);
    }
  }, [selectedRooms]);

  // 送信済みイベントID記録
  const reportedRef = React.useRef<Set<string>>(new Set());

  // クライアント可視レイテンシ送信 (初回表示イベントのみ)
  useEffect(() => {
    if (!events || events.length === 0) return;
    const fetchCompleted = lastFetchCompletedAtRef.current;
    const newSamples = events.filter(e => !reportedRef.current.has(e.id)).map(e => ({
      eventId: e.id,
      room: e.room,
      created: e.created,
      ingestedAtUtc: e.ingestedAtUtc,
      fetchedAtUtc: fetchCompleted ? new Date(fetchCompleted).toISOString() : null,
      renderAtUtc: new Date().toISOString()
    }));
    if (newSamples.length === 0) return;
    newSamples.forEach(s => reportedRef.current.add(s.eventId));
    // 非同期 fire & forget
    axios.post(`${API_BASE}/api/metrics/client-render`, { samples: newSamples }).catch(()=>{});
  }, [events]);

  useEffect(() => {
    fetchEvents();
    const interval = setInterval(fetchEvents, 30000); // 30秒ごとに更新
    return () => clearInterval(interval);
  }, [fetchEvents]);

  const getWeekDays = (date: Date) => {
    const start = startOfWeek(date, { weekStartsOn: 1 }); // 月曜日から開始
    return Array.from({ length: 5 }, (_, i) => addDays(start, i)); // 平日のみ
  };

  const getEventsForDateAndHour = (date: Date, hour: number) => {
    const targetDateTime = addHours(startOfHour(date), hour);
    // eventsが配列でない場合は空配列を返す
    if (!Array.isArray(events)) {
      return [];
    }
    return events.filter(event => {
      const eventStart = new Date(event.start.dateTime);
      const eventEnd = new Date(event.end.dateTime);
      return eventStart <= targetDateTime && eventEnd > targetDateTime;
    });
  };

  const formatEventTime = (event: Event) => {
    const start = new Date(event.start.dateTime);
    const end = new Date(event.end.dateTime);
    return `${format(start, 'HH:mm')} - ${format(end, 'HH:mm')}`;
  };

  const weekDays = viewMode === 'week' ? getWeekDays(selectedDate) : [selectedDate];
  const timeSlots = Array.from({ length: 10 }, (_, i) => i + 9); // 9:00 - 18:00
  // ルームごとのイベント件数
  const roomEventCount = React.useMemo(()=>{
    const map: Record<string, number> = {};
    for(const ev of events){
      if(!ev.room) continue; map[ev.room]=(map[ev.room]||0)+1;
    }
    return map;
  },[events]);

  return (
    <div className="App">
      <header className="app-header">
        <h1>会議室カレンダー (複数選択対応)</h1>
        <details style={{maxWidth:900,margin:'0 auto 0.75rem',background:'#ffffff',color:'#222',padding:'0.75rem 1rem',borderRadius:8}}>
          <summary style={{cursor:'pointer',fontWeight:600}}>会議室選択・件数確認</summary>
          <div style={{marginTop:'0.6rem',display:'flex',flexDirection:'column',gap:'0.6rem'}}>
            <div style={{display:'flex',gap:'0.5rem',flexWrap:'wrap',alignItems:'center'}}>
              <input placeholder="フィルタ (例: 3)" value={roomFilter} onChange={e=>setRoomFilter(e.target.value)} style={{padding:'0.35rem 0.6rem',fontSize:'0.8rem',border:'1px solid #ccc',borderRadius:4}} />
              <button type="button" onClick={()=> setSelectedRooms(allRooms)} style={{padding:'0.35rem 0.7rem',fontSize:'0.65rem',border:'1px solid #222',background:'#222',color:'#fff',borderRadius:4,cursor:'pointer'}}>全選択</button>
              <button type="button" onClick={()=> setSelectedRooms([allRooms[0]])} style={{padding:'0.35rem 0.7rem',fontSize:'0.65rem',border:'1px solid #999',background:'#999',color:'#fff',borderRadius:4,cursor:'pointer'}}>初期化</button>
              <span style={{fontSize:'0.65rem',opacity:0.75}}>選択: {selectedRooms.length} / {allRooms.length}</span>
            </div>
            <div style={{display:'grid',gridTemplateColumns:'repeat(auto-fill,minmax(150px,1fr))',gap:'0.4rem',maxHeight:180,overflowY:'auto'}}>
              {allRooms.filter(r=> r.toLowerCase().includes(roomFilter.toLowerCase())).map(r=>{
                const active = selectedRooms.includes(r);
                const count = roomEventCount[r]||0;
                return (
                  <label key={r} style={{
                    display:'flex',alignItems:'center',gap:'0.4rem',
                    background: active? (colorMap.get(r)||'#0d6efd'):'#eef2f7',
                    color: active? '#fff':'#222',
                    padding:'0.35rem 0.5rem',borderRadius:6,fontSize:'0.65rem',fontWeight:600,
                    cursor:'pointer',border:`1px solid ${active? (colorMap.get(r)||'#0d6efd'):'#d0d7e2'}`
                  }}>
                    <input type="checkbox" checked={active} onChange={()=> setSelectedRooms(prev=> prev.includes(r)? prev.filter(x=>x!==r): [...prev,r])} style={{width:14,height:14,accentColor:colorMap.get(r)||'#0d6efd'}} />
                    <span style={{flex:1,whiteSpace:'nowrap',overflow:'hidden',textOverflow:'ellipsis'}}>{r.replace('@bbslooklab.onmicrosoft.com','')}</span>
                    <span style={{fontSize:'0.55rem',background:'rgba(255,255,255,0.25)',padding:'0.1rem 0.3rem',borderRadius:4}}>{count}</span>
                  </label>
                );
              })}
            </div>
            <div style={{fontSize:'0.55rem',opacity:0.7,lineHeight:1.4}}>チェックで表示対象を切替。数値バッジは現在ロード済みイベント件数 (期間内)。</div>
          </div>
        </details>
        <details className="bulk-details" style={{margin:'0.5rem auto',maxWidth:900,padding:'0.75rem 1rem',borderRadius:8}}>
          <summary style={{cursor:'pointer',fontWeight:600}}>バルク会議作成ツール (PoC)</summary>
          <div className="bulk-content" style={{display:'flex',flexWrap:'wrap',gap:'0.75rem',marginTop:'0.75rem',alignItems:'flex-end'}}>
            <div>
              <label style={{fontSize:'0.7rem',fontWeight:600,display:'block'}}>対象会議室数</label>
              <span style={{fontSize:'0.85rem'}}>{bulkRooms.length} 室</span>
            </div>
            <div>
              <label style={{fontSize:'0.7rem',fontWeight:600,display:'block'}}>件数/室</label>
              <input type="number" min={1} max={50} value={bulkCount} onChange={e=>setBulkCount(parseInt(e.target.value||'1'))} style={{width:80}} />
            </div>
            <div className="checkbox-wrap">
              <label className="checkbox-inline" title="VisitorID を件名へ付与 (訪問者会議テスト用)">
                <input type="checkbox" checked={bulkVisitors} onChange={e=>setBulkVisitors(e.target.checked)} />
                <span>VisitorID付与</span>
              </label>
            </div>
            <div className="checkbox-wrap">
              <label className="checkbox-inline" title="skipCache=true で Webhook→Delta→Cache の遅延計測 (キャッシュ即時書込を抑止)">
                <input type="checkbox" checked={bulkSkipCache} onChange={e=>setBulkSkipCache(e.target.checked)} />
                <span>skipCache(計測)</span>
              </label>
            </div>
            <div style={{display:'flex',gap:'0.4rem'}}>
              <button disabled={bulkLoading || selectedRooms.length===0} onClick={async ()=>{
                setBulkLoading(true);setBulkMessage(null);
                try {
                  const today = new Date();
                  const dateStr = today.toISOString().slice(0,10);
                  const targetRooms = (bulkRooms.length>0? bulkRooms : selectedRooms); // フォールバック互換
                  const roomsParam = targetRooms.join(',');
                  const url = `${API_BASE}/api/CreateBulkEvents?rooms=${encodeURIComponent(roomsParam)}&countPerRoom=${bulkCount}&date=${dateStr}&withVisitors=${bulkVisitors}&skipCache=${bulkSkipCache}`;
                  const res = await axios.post(url, undefined, {timeout:60000});
                  setBulkMessage(`作成成功: total=${res.data?.summary?.totalEventsCreated ?? 'N/A'}`);
                  // 直書きした場合は即再フェッチ
                  if(!bulkSkipCache) fetchEvents();
                } catch(err:any){
                  setBulkMessage(`エラー: ${err.message||err}`);
                } finally { setBulkLoading(false);} 
              }} style={{padding:'0.55rem 1.1rem',background:'#0d6efd',color:'#fff',border:'none',borderRadius:6,fontWeight:600,cursor:'pointer'}}>
                {bulkLoading? '作成中...' : 'バルク作成実行'}
              </button>
              <button type="button" disabled={bulkLoading} onClick={()=> setBulkRooms(allRooms)} style={{padding:'0.4rem 0.8rem',background:'#222',color:'#fff',border:'none',borderRadius:6,fontSize:'0.65rem',cursor:'pointer'}}>全室</button>
              <button type="button" disabled={bulkLoading} onClick={()=> setBulkRooms([])} style={{padding:'0.4rem 0.8rem',background:'#6c757d',color:'#fff',border:'none',borderRadius:6,fontSize:'0.65rem',cursor:'pointer'}}>解除</button>
            </div>
            {bulkMessage && <div style={{fontSize:'0.7rem',color:'#333'}}>{bulkMessage}</div>}
            <div style={{flexBasis:'100%',display:'flex',flexWrap:'wrap',gap:'0.35rem',marginTop:'0.25rem',maxHeight:90,overflowY:'auto',padding:'0.25rem 0'}}>
              {allRooms.map(r=>{
                const active = bulkRooms.includes(r);
                return (
                  <span key={r} onClick={()=> setBulkRooms(prev=> prev.includes(r)? prev.filter(x=>x!==r): [...prev,r])} style={{
                    background: active? (colorMap.get(r)||'#0d6efd') : '#eef2f7',
                    color: active? '#fff':'#222',
                    border: `1px solid ${active? (colorMap.get(r)||'#0d6efd'):'#d0d7e2'}`,
                    padding:'0.25rem 0.5rem',
                    fontSize:'0.55rem',
                    fontWeight:600,
                    borderRadius:14,
                    cursor:'pointer'
                  }}>{r.replace('@bbslooklab.onmicrosoft.com','')}</span>
                );
              })}
            </div>
            <div style={{flexBasis:'100%',fontSize:'0.6rem',opacity:0.85,lineHeight:1.4}}>
              <strong>説明:</strong> VisitorID付与=訪問者シナリオ。skipCache(計測)=キャッシュ直書きを行わず処理パイプライン遅延を観測。未チェック時は作成後すぐ UI へ反映。
            </div>
          </div>
        </details>
        <div style={{display:'flex',flexWrap:'wrap',gap:'0.5rem',justifyContent:'center',maxWidth:900,margin:'0 auto 1rem'}}>
          {allRooms.map(r => {
            const active = selectedRooms.includes(r);
            return (
              <button key={r}
                style={{
                  background: active? (colorMap.get(r)||'#0984e3') : '#fff',
                  color: active? '#fff':'#333',
                  border: `2px solid ${colorMap.get(r)}`,
                  padding:'0.35rem 0.6rem',
                  borderRadius: '20px',
                  fontSize:'0.75rem',
                  cursor:'pointer',
                  fontWeight:600,
                  boxShadow: active? '0 2px 6px rgba(0,0,0,0.2)':'none'
                }}
                onClick={()=> setSelectedRooms(prev => prev.includes(r) ? prev.filter(x=>x!==r) : [...prev, r])}
              >{r.replace('@bbslooklab.onmicrosoft.com','')}</button>
            );
          })}
          <button style={{
            background:'#222',color:'#fff',border:'2px solid #222',borderRadius:'20px',padding:'0.35rem 0.6rem',fontSize:'0.75rem',cursor:'pointer'
          }} onClick={()=> setSelectedRooms(allRooms)}>全選択</button>
          <button style={{
            background:'#999',color:'#fff',border:'2px solid #999',borderRadius:'20px',padding:'0.35rem 0.6rem',fontSize:'0.75rem',cursor:'pointer'
          }} onClick={()=> setSelectedRooms([allRooms[0]])}>リセット</button>
        </div>
        
        {/* パフォーマンス統計 */}
        <div className="performance-stats">
          <div className="stat-item">
            <span className="stat-label">応答時間:</span>
            <span className={`stat-value ${performanceStats.responseTime > 10000 ? 'warning' : 'success'}`}>
              {performanceStats.responseTime}ms
            </span>
          </div>
          <div className="stat-item">
            <span className="stat-label">平均応答時間:</span>
            <span className={`stat-value ${performanceStats.averageResponseTime > 10000 ? 'warning' : 'success'}`}>
              {Math.round(performanceStats.averageResponseTime)}ms
            </span>
          </div>
          <div className="stat-item">
            <span className="stat-label">P95応答時間:</span>
            <span className={`stat-value ${performanceStats.p95ResponseTime > 10000 ? 'warning' : 'success'}`}>
              {performanceStats.p95ResponseTime}ms
            </span>
          </div>
          <div className="stat-item">
            <span className="stat-label">リクエスト数:</span>
            <span className="stat-value">{performanceStats.requestCount}</span>
          </div>
        </div>

        {/* ビューモード選択 */}
        <div className="view-mode-selector">
          <button 
            className={viewMode === 'day' ? 'active' : ''}
            onClick={() => setViewMode('day')}
          >
            日表示
          </button>
          <button 
            className={viewMode === 'week' ? 'active' : ''}
            onClick={() => setViewMode('week')}
          >
            週表示
          </button>
        </div>

        {/* 日付ナビゲーション */}
        <div className="date-navigation">
          <button onClick={() => setSelectedDate(new Date(selectedDate.getTime() - (viewMode === 'week' ? 7 : 1) * 24 * 60 * 60 * 1000))}>
            ← 前の{viewMode === 'week' ? '週' : '日'}
          </button>
          <span className="current-date">
            {viewMode === 'week' 
              ? `${format(weekDays[0], 'yyyy年MM月dd日', { locale: ja })} - ${format(weekDays[4], 'MM月dd日', { locale: ja })}`
              : format(selectedDate, 'yyyy年MM月dd日（eee）', { locale: ja })
            }
          </span>
          <button onClick={() => setSelectedDate(new Date(selectedDate.getTime() + (viewMode === 'week' ? 7 : 1) * 24 * 60 * 60 * 1000))}>
            次の{viewMode === 'week' ? '週' : '日'} →
          </button>
        </div>

        <button onClick={fetchEvents} disabled={loading} className="refresh-button">
          {loading ? '読み込み中...' : '更新'}
        </button>
      </header>

      {error && (
        <div className="error-message">
          エラー: {error}
        </div>
      )}

      <main className="calendar-container">
        <div className={`calendar-grid ${viewMode}`}>
          {/* ヘッダー行 */}
          <div className="time-header">時間</div>
          {weekDays.map(day => (
            <div key={day.toISOString()} className={`date-header ${isToday(day) ? 'today' : ''}`}>
              <div className="date-day">{format(day, 'eee', { locale: ja })}</div>
              <div className="date-number">{format(day, 'dd')}</div>
            </div>
          ))}

          {/* カレンダーボディ */}
          {timeSlots.map(hour => (
            <React.Fragment key={hour}>
              <div className="time-slot">
                {hour}:00
              </div>
              {weekDays.map(day => {
                const dayEvents = getEventsForDateAndHour(day, hour);
                return (
                  <div key={`${day.toISOString()}-${hour}`} className="calendar-cell">
                    {Array.isArray(dayEvents) && dayEvents.map(event => (
                      <div key={event.id+event.room} className="event-card" style={{
                        background: `linear-gradient(135deg, ${(colorMap.get(event.room||''))} 0%, #222 100%)`,
                        borderLeftColor: colorMap.get(event.room||'')
                      }}>
                        <div className="event-time-badge">
                          {formatEventTime(event)}
                        </div>
                        <div className="event-subject">
                          {event.subject}
                        </div>
                        <div style={{fontSize:'0.6rem',opacity:0.9,marginBottom:'0.2rem'}}>{event.room?.split('@')[0]}</div>
                        <div className="event-organizer">
                          主催者: {event.organizer.emailAddress.name || event.organizer.emailAddress.address}
                        </div>
                        {event.visitorInfo?.hasVisitor && (
                          <div className="visitor-badge">
                            👥 訪問者: {event.visitorInfo.extractedNames.join(', ')}
                          </div>
                        )}
                        {event.attendees && event.attendees.length > 0 && (
                          <div className="attendees-count">
                            参加者: {event.attendees.length}名
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                );
              })}
            </React.Fragment>
          ))}
        </div>
      </main>
    </div>
  );
}

export default App;
