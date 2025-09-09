using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using FunctionApp.Services;
using System.Text;
using System.Text.Json;
using System.Net;

namespace FunctionApp.Functions
{
    public class RealtimeEventsFunction
    {
        private readonly ILogger<RealtimeEventsFunction> _logger;
        private readonly IEventCacheStore _eventCache;

        public RealtimeEventsFunction(ILogger<RealtimeEventsFunction> logger, IEventCacheStore eventCache)
        {
            _logger = logger;
            _eventCache = eventCache;
        }

        [Function("RealtimeEvents")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequestData req,
            FunctionContext executionContext)
        {
            try
            {
                _logger.LogInformation("リアルタイムイベントストリーム開始");

                var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                var roomUpn = query.Get("roomUpn");
                var pollIntervalMs = int.Parse(query.Get("pollIntervalMs") ?? "2000");

                var response = req.CreateResponse();
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
                response.Headers.Add("Content-Type", "text/event-stream");
                response.Headers.Add("Cache-Control", "no-cache");
                response.Headers.Add("Connection", "keep-alive");

                var initialData = await GetCurrentEventData(roomUpn);
                await SendSSEData(response, "initial", initialData);

                var lastUpdateTime = DateTime.UtcNow;
                var pollCount = 0;
                const int maxPolls = 150;

                var cancellationToken = executionContext.CancellationToken;

                while (!cancellationToken.IsCancellationRequested && pollCount < maxPolls)
                {
                    try
                    {
                        await Task.Delay(pollIntervalMs, cancellationToken);
                        pollCount++;

                        var currentData = await GetCurrentEventData(roomUpn);
                        var hasChanges = await HasDataChanged(roomUpn, lastUpdateTime);

                        if (hasChanges)
                        {
                            await SendSSEData(response, "update", currentData);
                            lastUpdateTime = DateTime.UtcNow;
                            _logger.LogInformation($"リアルタイム更新送信: {roomUpn} (Poll #{pollCount})");
                        }
                        else
                        {
                            await SendSSEData(response, "heartbeat", new
                            {
                                timestamp = DateTime.UtcNow,
                                pollCount = pollCount,
                                status = "alive"
                            });
                        }

                        await response.Body.FlushAsync();
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("リアルタイムストリーム: クライアント切断");
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"リアルタイムストリーム エラー: {ex.Message}");
                        await SendSSEData(response, "error", new
                        {
                            error = ex.Message,
                            timestamp = DateTime.UtcNow
                        });
                    }
                }

                await SendSSEData(response, "end", new
                {
                    message = "ストリーム終了",
                    totalPolls = pollCount,
                    timestamp = DateTime.UtcNow
                });

                _logger.LogInformation($"リアルタイムストリーム終了: {pollCount}回ポーリング");

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"リアルタイムイベントストリーム エラー: {ex.Message}");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                errorResponse.Headers.Add("Content-Type", "application/json");
                var errorJson = JsonSerializer.Serialize(new
                {
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                });
                errorResponse.WriteString(errorJson);
                return errorResponse;
            }
        }

        private async Task<object> GetCurrentEventData(string? roomUpn)
        {
            try
            {
                var events = (await _eventCache.GetAllEventsAsync(roomUpn ?? "")).ToList();

                string? GetString(JsonElement el, string name) => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                bool GetBool(JsonElement el, string name) => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

                if (string.IsNullOrEmpty(roomUpn))
                {
                    var grouped = events.GroupBy(e => GetString(e, "roomUpn") ?? "");
                    return new
                    {
                        type = "all_rooms",
                        totalEvents = events.Count,
                        eventsByRoom = grouped.ToDictionary(
                            g => g.Key,
                            g => g.Select(e => new
                            {
                                id = GetString(e, "id"),
                                subject = GetString(e, "Subject"),
                                start = GetString(e, "start"),
                                end = GetString(e, "end"),
                                organizer = GetOrganizerName(e),
                                visitorId = GetString(e, "visitorId"),
                                hasVisitor = !string.IsNullOrEmpty(GetString(e, "visitorId")),
                                isCancelled = GetBool(e, "isCancelled"),
                                lastModified = GetString(e, "lastModified")
                            }).ToList()
                        ),
                        timestamp = DateTime.UtcNow
                    };
                }
                else
                {
                    var filtered = events
                        .Where(e => string.Equals(GetString(e, "roomUpn"), roomUpn, StringComparison.OrdinalIgnoreCase))
                        .Select(e => new
                        {
                            id = GetString(e, "id"),
                            subject = GetString(e, "Subject"),
                            start = GetString(e, "start"),
                            end = GetString(e, "end"),
                            organizer = GetOrganizerName(e),
                            visitorId = GetString(e, "visitorId"),
                            hasVisitor = !string.IsNullOrEmpty(GetString(e, "visitorId")),
                            isCancelled = GetBool(e, "isCancelled"),
                            lastModified = GetString(e, "lastModified")
                        })
                        .ToList();

                    return new
                    {
                        type = "single_room",
                        room = roomUpn,
                        eventCount = filtered.Count,
                        events = filtered,
                        timestamp = DateTime.UtcNow
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"イベントデータ取得エラー: {ex.Message}");
                return new
                {
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                };
            }
        }

        private async Task<bool> HasDataChanged(string? roomUpn, DateTime lastCheck)
        {
            try
            {
                var events = await _eventCache.GetAllEventsAsync(roomUpn ?? "");
                foreach (var e in events)
                {
                    if (e.TryGetProperty("lastModified", out var lm) && lm.ValueKind == JsonValueKind.String && DateTime.TryParse(lm.GetString(), out var dt))
                    {
                        if (dt > lastCheck) return true;
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"データ変更チェック エラー: {ex.Message}");
                return false;
            }
        }

    private async Task SendSSEData(HttpResponseData response, string eventType, object data)
        {
            try
            {
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                var sseData = $"event: {eventType}\ndata: {json}\n\n";
                var bytes = Encoding.UTF8.GetBytes(sseData);
        await response.Body.WriteAsync(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError($"SSEデータ送信エラー: {ex.Message}");
            }
        }

        // Organizer -> EmailAddress -> Name を取り出す補助
        private static string? GetOrganizerName(JsonElement el)
        {
            if (el.TryGetProperty("Organizer", out var org) && org.ValueKind == JsonValueKind.Object)
            {
                if (org.TryGetProperty("EmailAddress", out var ema) && ema.ValueKind == JsonValueKind.Object)
                {
                    if (ema.TryGetProperty("Name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                        return nameEl.GetString();
                }
            }
            return null;
        }
    }
}
