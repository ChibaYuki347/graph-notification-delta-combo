using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using FunctionApp.Utils;
using System.Net;
using System.Text.Json;

namespace FunctionApp.Functions
{
    public class CreateBulkEventsFunction
    {
        private readonly ILogger<CreateBulkEventsFunction> _logger;
        private readonly GraphServiceClient _graphClient;
        private readonly Services.IEventCacheStore _cache;

        public CreateBulkEventsFunction(ILogger<CreateBulkEventsFunction> logger, GraphServiceClient graphClient, Services.IEventCacheStore cache)
        {
            _logger = logger;
            _graphClient = graphClient;
            _cache = cache;
        }

        [Function("CreateBulkEvents")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            try
            {
                _logger.LogInformation("大量予定作成API開始");

                // パラメータ取得
                var queryParams = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(req.Url.Query))
                {
                    var query = req.Url.Query.TrimStart('?');
                    foreach (var param in query.Split('&'))
                    {
                        var parts = param.Split('=', 2);
                        if (parts.Length == 2)
                        {
                            queryParams[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
                        }
                    }
                }
                
                var roomUpns = queryParams.GetValueOrDefault("rooms", "")?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                var countPerRoom = int.Parse(queryParams.GetValueOrDefault("countPerRoom", "10"));
                var startHour = int.Parse(queryParams.GetValueOrDefault("startHour", "9"));
                var endHour = int.Parse(queryParams.GetValueOrDefault("endHour", "18"));
                var targetDate = DateTime.Parse(queryParams.GetValueOrDefault("date", DateTime.Today.ToString("yyyy-MM-dd")));
                var withVisitors = bool.Parse(queryParams.GetValueOrDefault("withVisitors", "true"));
                var skipCache = bool.Parse(queryParams.GetValueOrDefault("skipCache", "false"));

                if (!roomUpns.Any())
                {
                    roomUpns = new[] {
                        "ConfRoom1@bbslooklab.onmicrosoft.com",
                        "ConfRoom2@bbslooklab.onmicrosoft.com",
                        "リソース02@bbslooklab.onmicrosoft.com",
                        "リソース03@bbslooklab.onmicrosoft.com"
                    };
                }

                var results = new List<object>();
                var totalStartTime = DateTime.UtcNow;

                foreach (var roomUpn in roomUpns)
                {
                    var roomStartTime = DateTime.UtcNow;
                    var roomEvents = new List<object>();

                    for (int i = 0; i < countPerRoom; i++)
                    {
                        try
                        {
                            var eventStartTime = DateTime.UtcNow;
                            
                            // 時間をランダムに分散
                            var random = new Random();
                            var startTimeHour = random.Next(startHour, endHour - 1);
                            var startTimeMinute = random.Next(0, 60);
                            var durationMinutes = random.Next(30, 121); // 30分〜2時間

                            var eventStart = targetDate.Date.AddHours(startTimeHour).AddMinutes(startTimeMinute);
                            var eventEnd = eventStart.AddMinutes(durationMinutes);

                            // VisitorID生成（50%の確率で来客あり）
                            string? visitorId = null;
                            if (withVisitors && random.NextDouble() < 0.5)
                            {
                                visitorId = Guid.NewGuid().ToString();
                            }

                            var subject = $"負荷テスト会議 #{i + 1:D3}";
                            if (visitorId != null)
                            {
                                subject += " [来客対応]";
                            }

                            // Graph API用のEvent作成
                            var graphEvent = new Event
                            {
                                Subject = subject,
                                Start = new DateTimeTimeZone
                                {
                                    DateTime = eventStart.ToString("yyyy-MM-ddTHH:mm:ss.0000000"),
                                    TimeZone = "Asia/Tokyo"
                                },
                                End = new DateTimeTimeZone
                                {
                                    DateTime = eventEnd.ToString("yyyy-MM-ddTHH:mm:ss.0000000"),
                                    TimeZone = "Asia/Tokyo"
                                },
                                Attendees = new List<Attendee>
                                {
                                    new()
                                    {
                                        EmailAddress = new EmailAddress
                                        {
                                            Address = roomUpn,
                                            Name = $"会議室 {roomUpn.Split('@')[0]}"
                                        },
                                        Type = AttendeeType.Resource
                                    }
                                },
                                Organizer = new Recipient
                                {
                                    EmailAddress = new EmailAddress
                                    {
                                        Address = "testuser@bbslooklab.onmicrosoft.com",
                                        Name = "負荷テストユーザー"
                                    }
                                }
                            };

                            // VisitorIDをBodyに埋め込み（実際の来客管理アドイン形式）
                            if (visitorId != null)
                            {
                                graphEvent.Body = new ItemBody
                                {
                                    ContentType = BodyType.Html,
                                    Content = $@"
                                        <div>負荷テスト用の会議です。</div>
                                        <div>来客情報が含まれています。</div>
                                        <br/>
                                        <span style='display:none;'>VisitorID:{visitorId}</span>
                                        <div style='color:#666; font-size:0.8em;'>
                                        ^^^^^^^^^<br/>
                                        【来客管理アドインからのお願い】<br/>
                                        ^^^^^^^^^
                                        </div>"
                                };
                            }

                            // Graph APIで会議室ベースで会議作成
                            var createdEvent = await _graphClient.Users[roomUpn].Events.PostAsync(graphEvent);

                            // キャッシュ保存 (skipCache 指定時はパイプライン経由の遅延測定用に抑止)
                            if (createdEvent != null && !skipCache)
                            {
                                try
                                {
                                    await _cache.UpsertAsync(roomUpn, createdEvent, visitorId);
                                }
                                catch (Exception cacheEx)
                                {
                                    _logger.LogWarning(cacheEx, "キャッシュ保存失敗 room={roomUpn} eventId={eventId}", roomUpn, createdEvent.Id);
                                }
                            }
                            
                            var eventEndTime = DateTime.UtcNow;
                            var eventDuration = (eventEndTime - eventStartTime).TotalMilliseconds;

                            roomEvents.Add(new
                            {
                                eventId = createdEvent?.Id,
                                subject = subject,
                                start = eventStart,
                                end = eventEnd,
                                room = roomUpn,
                                visitorId = visitorId,
                                hasVisitor = visitorId != null,
                                creationTimeMs = eventDuration,
                                cached = !skipCache
                            });

                            _logger.LogInformation($"会議作成完了: {subject} ({roomUpn}) - {eventDuration:F1}ms");

                            // API制限を避けるため短い間隔をあける
                            await Task.Delay(100);
                        }
                        catch (ODataError ex)
                        {
                            _logger.LogError($"会議作成エラー (Room: {roomUpn}, Index: {i}): {ex.Error?.Message}");
                            roomEvents.Add(new
                            {
                                error = true,
                                message = ex.Error?.Message,
                                room = roomUpn,
                                index = i
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"予期しないエラー (Room: {roomUpn}, Index: {i}): {ex.Message}");
                            roomEvents.Add(new
                            {
                                error = true,
                                message = ex.Message,
                                room = roomUpn,
                                index = i
                            });
                        }
                    }

                    var roomEndTime = DateTime.UtcNow;
                    var roomDuration = (roomEndTime - roomStartTime).TotalMilliseconds;

                    // null セーフ集計: error プロパティが存在し true のものをエラー扱い
                    int errorCount = 0;
                    int successCount = 0;
                    foreach (var ev in roomEvents)
                    {
                        var dyn = (dynamic)ev;
                        var errorProp = dyn.GetType().GetProperty("error");
                        bool isError = false;
                        if (errorProp != null)
                        {
                            var val = errorProp.GetValue(ev, null);
                            if (val is bool b && b) isError = true;
                        }
                        if (isError) errorCount++; else successCount++;
                    }

                    results.Add(new
                    {
                        room = roomUpn,
                        eventsCreated = successCount,
                        eventsWithErrors = errorCount,
                        totalTimeMs = roomDuration,
                        averageTimePerEventMs = roomDuration / Math.Max(roomEvents.Count, 1),
                        events = roomEvents
                    });

                    _logger.LogInformation($"会議室 {roomUpn} 完了: {roomEvents.Count}件作成, {roomDuration:F1}ms");
                }

                var totalEndTime = DateTime.UtcNow;
                var totalDuration = (totalEndTime - totalStartTime).TotalMilliseconds;

                var summary = new
                {
                    success = true,
                    summary = new
                    {
                        totalRooms = roomUpns.Length,
                        requestedEventsPerRoom = countPerRoom,
                        totalEventsCreated = results.Sum(r => ((dynamic)r).eventsCreated),
                        totalEventsWithErrors = results.Sum(r => ((dynamic)r).eventsWithErrors),
                        totalTimeMs = totalDuration,
                        averageTimePerRoomMs = totalDuration / roomUpns.Length,
                        targetDate = targetDate.ToString("yyyy-MM-dd"),
                        timeRange = $"{startHour:D2}:00 - {endHour:D2}:00",
                        withVisitorsEnabled = withVisitors,
                        skipCache
                    },
                    performanceMetrics = new
                    {
                        totalDurationMs = totalDuration,
                        eventsPerSecond = results.Sum(r => ((dynamic)r).eventsCreated) / (totalDuration / 1000.0),
                        averageEventCreationMs = totalDuration / Math.Max(results.Sum(r => ((dynamic)r).eventsCreated), 1),
                        p95Target = "P95 ≤ 10秒",
                        status = totalDuration <= 10000 ? "OK" : "SLOW"
                    },
                    rooms = results,
                    timestamp = DateTime.UtcNow
                };

                _logger.LogInformation($"大量予定作成完了: {results.Sum(r => ((dynamic)r).eventsCreated)}件作成, {totalDuration:F1}ms");

                var response = req.CreateResponse(HttpStatusCode.OK);
                response.Headers.Add("Content-Type", "application/json");
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, x-functions-key");
                await response.WriteStringAsync(JsonSerializer.Serialize(summary, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError($"大量予定作成API エラー: {ex.Message}");
                
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                errorResponse.Headers.Add("Content-Type", "application/json");
                errorResponse.Headers.Add("Access-Control-Allow-Origin", "*");
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
                {
                    success = false,
                    error = ex.Message,
                    timestamp = DateTime.UtcNow
                }, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                }));
                return errorResponse;
            }
        }
    }
}
