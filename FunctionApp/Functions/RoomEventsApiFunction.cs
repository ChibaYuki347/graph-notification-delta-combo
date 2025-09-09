using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using FunctionApp.Services;

namespace FunctionApp.Functions;

public class RoomEventsApiFunction
{
    private readonly ILogger<RoomEventsApiFunction> _logger;
    private readonly IEventCacheStore _cache;

    public RoomEventsApiFunction(ILoggerFactory lf, IEventCacheStore cache)
    {
        _logger = lf.CreateLogger<RoomEventsApiFunction>();
        _cache = cache;
    }

    [Function("GetRoomEvents")]
    public async Task<HttpResponseData> GetRoomEvents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "rooms/{roomUpn}/events")] HttpRequestData req,
        string roomUpn)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            _logger.LogInformation("Getting events for room: {roomUpn}", roomUpn);

            var rawEvents = (await _cache.GetAllEventsAsync(roomUpn)).ToList();
            var rawCount = rawEvents.Count;
            bool disableFilter = req.Url.Query.Contains("raw=true", StringComparison.OrdinalIgnoreCase);
            var now = DateTimeOffset.UtcNow;
            var today = now.Date;

            var filteredEvents = new List<object>();
            int parsedOk = 0;
            int skippedOutsideWindow = 0;
            int parseFailed = 0;
            foreach (var je in rawEvents)
            {
                try
                {
                    string? GetString(string name)
                    {
                        return je.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                    }

                    var startStr = GetString("start");
                    var endStr = GetString("end");
                    if (!DateTime.TryParse(startStr, out var startDt))
                    {
                        parseFailed++;
                        continue; // 日付取得できなければスキップ
                    }
                    if (!disableFilter && (startDt.Date < today || startDt.Date > today.AddDays(7)))
                    {
                        skippedOutsideWindow++;
                        continue; // 範囲外
                    }
                    parsedOk++;

                    DateTime? endDt = null;
                    if (DateTime.TryParse(endStr, out var tmpEnd)) endDt = tmpEnd;

                    // 任意フィールド抽出
                    var id = GetString("id") ?? Guid.NewGuid().ToString("N");
                    var subject = GetString("Subject") ?? "(no subject)";
                    string? visitorId = GetString("visitorId");

                    // Organizer (JSON構造: Organizer -> EmailAddress -> Name / Address)
                    string? organizerName = null;
                    string? organizerEmail = null;
                    if (je.TryGetProperty("Organizer", out var organizerEl) && organizerEl.ValueKind == JsonValueKind.Object)
                    {
                        if (organizerEl.TryGetProperty("EmailAddress", out var emailEl) && emailEl.ValueKind == JsonValueKind.Object)
                        {
                            if (emailEl.TryGetProperty("Name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                                organizerName = nameEl.GetString();
                            if (emailEl.TryGetProperty("Address", out var addrEl) && addrEl.ValueKind == JsonValueKind.String)
                                organizerEmail = addrEl.GetString();
                        }
                    }

                    // Attendees 配列長
                    int attendeeCount = 0;
                    if (je.TryGetProperty("Attendees", out var attendeesEl) && attendeesEl.ValueKind == JsonValueKind.Array)
                    {
                        attendeeCount = attendeesEl.GetArrayLength();
                    }

                    bool isCancelled = false;
                    if (je.TryGetProperty("isCancelled", out var cancelEl) && cancelEl.ValueKind == JsonValueKind.True)
                        isCancelled = true;

                    DateTime? createdDt = null;
                    if (je.TryGetProperty("created", out var createdEl) && createdEl.ValueKind == JsonValueKind.String && DateTime.TryParse(createdEl.GetString(), out var tmpCreated))
                        createdDt = tmpCreated;

                    DateTime? lastModDt = null;
                    if (je.TryGetProperty("lastModified", out var lastModEl) && lastModEl.ValueKind == JsonValueKind.String && DateTime.TryParse(lastModEl.GetString(), out var tmpMod))
                        lastModDt = tmpMod;

                    filteredEvents.Add(new
                    {
                        id,
                        subject,
                        start = startDt,
                        end = endDt,
                        organizer = organizerName ?? "Unknown",
                        organizerEmail = organizerEmail,
                        visitorId,
                        hasVisitor = !string.IsNullOrEmpty(visitorId),
                        isCancelled,
                        attendeeCount,
                        created = createdDt,
                        lastModified = lastModDt
                    });
                }
                catch (Exception parseEx)
                {
                    _logger.LogWarning(parseEx, "イベント解析失敗 room={roomUpn}", roomUpn);
                }
            }

            filteredEvents = filteredEvents
                .OrderBy(e => (DateTime?)e.GetType().GetProperty("start")?.GetValue(e) ?? DateTime.MaxValue)
                .ToList();

            _logger.LogInformation("Room {roomUpn} raw={raw} parsedOk={parsedOk} skippedWindow={skipped} parseFailed={failed} final={final}", roomUpn, rawCount, parsedOk, skippedOutsideWindow, parseFailed, filteredEvents.Count);

            var responseTime = DateTime.UtcNow - startTime;
            
            var result = new
            {
                room = roomUpn,
                eventCount = filteredEvents.Count,
                events = filteredEvents,
                rawCount,
                parsedOk,
                skippedOutsideWindow,
                parseFailed,
                filterDisabled = disableFilter,
                responseTimeMs = responseTime.TotalMilliseconds,
                timestamp = DateTimeOffset.UtcNow,
                performance = new
                {
                    cacheHit = true,
                    responseTimeMs = responseTime.TotalMilliseconds,
                    target = "P95 ≤ 10秒",
                    status = responseTime.TotalSeconds <= 10 ? "OK" : "SLOW"
                }
            };

            _logger.LogInformation("Room events retrieved in {responseTime}ms for {roomUpn}: {eventCount} events", 
                responseTime.TotalMilliseconds, roomUpn, filteredEvents.Count);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
            await response.WriteStringAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return response;
        }
        catch (Exception ex)
        {
            var responseTime = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "Failed to get events for room {roomUpn} in {responseTime}ms", roomUpn, responseTime.TotalMilliseconds);
            
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            errorResponse.Headers.Add("Access-Control-Allow-Origin", "*");
            await errorResponse.WriteStringAsync($"Error getting events: {ex.Message}");
            return errorResponse;
        }
    }

    [Function("GetAllRooms")]
    public async Task<HttpResponseData> GetAllRooms(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "rooms")] HttpRequestData req)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            // 設定から会議室一覧を取得（本来はGraphAPIから取得）
            var rooms = new[]
            {
                new { upn = "ConfRoom1@bbslooklab.onmicrosoft.com", name = "Conference Room 1", capacity = 10, floor = "1F" },
                new { upn = "ConfRoom2@bbslooklab.onmicrosoft.com", name = "Conference Room 2", capacity = 8, floor = "1F" },
                new { upn = "リソース02@bbslooklab.onmicrosoft.com", name = "リソース02", capacity = 6, floor = "2F" },
                new { upn = "リソース03@bbslooklab.onmicrosoft.com", name = "リソース03", capacity = 4, floor = "2F" }
            };

            var responseTime = DateTime.UtcNow - startTime;
            
            var result = new
            {
                rooms = rooms,
                responseTimeMs = responseTime.TotalMilliseconds,
                timestamp = DateTimeOffset.UtcNow
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
            await response.WriteStringAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return response;
        }
        catch (Exception ex)
        {
            var responseTime = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "Failed to get rooms in {responseTime}ms", responseTime.TotalMilliseconds);
            
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            errorResponse.Headers.Add("Access-Control-Allow-Origin", "*");
            await errorResponse.WriteStringAsync($"Error getting rooms: {ex.Message}");
            return errorResponse;
        }
    }

    [Function("OptionsRooms")]
    public HttpResponseData OptionsRooms(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "rooms/{*route}")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
        response.Headers.Add("Access-Control-Max-Age", "3600");
        return response;
    }
}
