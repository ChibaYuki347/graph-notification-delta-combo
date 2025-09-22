using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using FunctionApp.Services;

namespace FunctionApp.Functions;

public class AllRoomEventsApiFunction
{
    private readonly ILogger<AllRoomEventsApiFunction> _logger;
    private readonly IEventCacheStore _cache;

    public AllRoomEventsApiFunction(ILoggerFactory lf, IEventCacheStore cache)
    {
        _logger = lf.CreateLogger<AllRoomEventsApiFunction>();
        _cache = cache;
    }

    [Function("GetAllRoomEvents")]
    public async Task<HttpResponseData> GetAllRoomEvents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "room-events")] HttpRequestData req)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            _logger.LogInformation("Getting events for all rooms");

            // 設定から会議室一覧を取得（UI期待値の16室）
            var roomUpns = new[]
            {
                "ConfRoom1@bbslooklab.onmicrosoft.com",
                "ConfRoom2@bbslooklab.onmicrosoft.com", 
                "ConfRoom3@bbslooklab.onmicrosoft.com",
                "ConfRoom4@bbslooklab.onmicrosoft.com",
                "ConfRoom5@bbslooklab.onmicrosoft.com",
                "ConfRoom6@bbslooklab.onmicrosoft.com",
                "ConfRoom7@bbslooklab.onmicrosoft.com",
                "ConfRoom8@bbslooklab.onmicrosoft.com",
                "ConfRoom9@bbslooklab.onmicrosoft.com",
                "ConfRoom10@bbslooklab.onmicrosoft.com",
                "ConfRoom11@bbslooklab.onmicrosoft.com",
                "ConfRoom12@bbslooklab.onmicrosoft.com",
                "ConfRoom13@bbslooklab.onmicrosoft.com",
                "ConfRoom14@bbslooklab.onmicrosoft.com",
                "ConfRoom15@bbslooklab.onmicrosoft.com",
                "ConfRoom16@bbslooklab.onmicrosoft.com"
            };

            // 時間フィルタリング設定
            bool disableFilter = req.Url.Query.Contains("raw=true", StringComparison.OrdinalIgnoreCase);
            var now = DateTimeOffset.UtcNow;
            var today = now.Date;

            var allEvents = new List<object>();
            var roomStats = new Dictionary<string, object>();
            int totalRawCount = 0;
            int totalParsedOk = 0;
            int totalSkippedOutsideWindow = 0;
            int totalParseFailed = 0;

            foreach (var roomUpn in roomUpns)
            {
                try
                {
                    var rawEvents = (await _cache.GetAllEventsAsync(roomUpn)).ToList();
                    var rawCount = rawEvents.Count;
                    totalRawCount += rawCount;

                    var roomEvents = new List<object>();
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
                                continue;
                            }

                            // 時間フィルタリング (raw=true の場合はスキップ)
                            if (!disableFilter && (startDt.Date < today || startDt.Date > today.AddDays(7)))
                            {
                                skippedOutsideWindow++;
                                continue;
                            }
                            parsedOk++;

                            DateTime? endDt = null;
                            if (DateTime.TryParse(endStr, out var tmpEnd)) endDt = tmpEnd;

                            var id = GetString("id") ?? Guid.NewGuid().ToString("N");
                            var subject = GetString("Subject") ?? "(no subject)";
                            string? visitorId = GetString("visitorId");

                            // Organizer情報の抽出
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

                            // Attendees数
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

                            var eventObj = new
                            {
                                id,
                                subject,
                                start = startDt,
                                end = endDt,
                                room = roomUpn,
                                roomName = roomUpn.Split('@')[0], // ConfRoom1 部分を抽出
                                organizer = organizerName ?? "Unknown",
                                organizerEmail = organizerEmail,
                                visitorId,
                                hasVisitor = !string.IsNullOrEmpty(visitorId),
                                isCancelled,
                                attendeeCount,
                                created = createdDt,
                                lastModified = lastModDt,
                                ingestedAtUtc = je.TryGetProperty("ingestedAtUtc", out var ingestEl) && ingestEl.ValueKind == JsonValueKind.String ? ingestEl.GetString() : null
                            };

                            roomEvents.Add(eventObj);
                            allEvents.Add(eventObj);
                        }
                        catch (Exception parseEx)
                        {
                            _logger.LogWarning(parseEx, "イベント解析失敗 room={roomUpn}", roomUpn);
                            parseFailed++;
                        }
                    }

                    totalParsedOk += parsedOk;
                    totalSkippedOutsideWindow += skippedOutsideWindow;
                    totalParseFailed += parseFailed;

                    roomStats[roomUpn] = new
                    {
                        rawCount,
                        parsedOk,
                        skippedOutsideWindow,
                        parseFailed,
                        finalCount = roomEvents.Count
                    };

                    _logger.LogInformation("Room {roomUpn}: raw={raw} parsed={parsed} skipped={skipped} failed={failed} final={final}", 
                        roomUpn, rawCount, parsedOk, skippedOutsideWindow, parseFailed, roomEvents.Count);
                }
                catch (Exception roomEx)
                {
                    _logger.LogWarning(roomEx, "Failed to get events for room {roomUpn}", roomUpn);
                    roomStats[roomUpn] = new { error = roomEx.Message };
                }
            }

            // 時間順でソート
            allEvents = allEvents
                .OrderBy(e => (DateTime?)e.GetType().GetProperty("start")?.GetValue(e) ?? DateTime.MaxValue)
                .ToList();

            var responseTime = DateTime.UtcNow - startTime;
            
            var result = new
            {
                eventCount = allEvents.Count,
                events = allEvents,
                stats = new
                {
                    totalRawCount,
                    totalParsedOk,
                    totalSkippedOutsideWindow,
                    totalParseFailed,
                    finalCount = allEvents.Count,
                    roomCount = roomUpns.Length,
                    roomStats
                },
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

            _logger.LogInformation("All room events retrieved in {responseTime}ms: {eventCount} events from {roomCount} rooms", 
                responseTime.TotalMilliseconds, allEvents.Count, roomUpns.Length);

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
            _logger.LogError(ex, "Failed to get all room events in {responseTime}ms", responseTime.TotalMilliseconds);
            
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            errorResponse.Headers.Add("Access-Control-Allow-Origin", "*");
            await errorResponse.WriteStringAsync($"Error getting all room events: {ex.Message}");
            return errorResponse;
        }
    }

    [Function("OptionsAllRoomEvents")]
    public HttpResponseData OptionsAllRoomEvents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "room-events")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
        response.Headers.Add("Access-Control-Max-Age", "3600");
        return response;
    }
}