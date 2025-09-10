
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.CalendarView.Delta;
using FunctionApp.Services;
using FunctionApp.Models;
using FunctionApp.Utils;

namespace FunctionApp.Functions;

public class WindowOptions
{
    public int DaysPast { get; set; } = 1;
    public int DaysFuture { get; set; } = 7;
}

public class DeltaWorkerFunction
{
    private readonly ILogger _logger;
    private readonly GraphServiceClient _graph;
    private readonly IStateStore _state;
    private readonly IEventCacheStore _cache;
    private readonly VisitorIdExtractor _visitor;
    private readonly WindowOptions _window;

    public DeltaWorkerFunction(ILoggerFactory lf, GraphServiceClient graph, IStateStore state, IEventCacheStore cache, VisitorIdExtractor visitor, WindowOptions window)
    {
        _logger = lf.CreateLogger<DeltaWorkerFunction>();
        _graph = graph;
        _state = state;
        _cache = cache;
        _visitor = visitor;
        _window = window;
    }

    [Function("DeltaWorker")]
    public async Task RunAsync([QueueTrigger("%Webhook:NotificationQueue%", Connection = "AzureWebJobsStorage")] string message)
    {
        _logger.LogInformation("DeltaWorker received raw message: {raw}", message);
        ChangeMessage? msg = null;
        try
        {
            try
            {
                if (message.TrimStart().StartsWith("{"))
                {
                    msg = JsonSerializer.Deserialize<ChangeMessage>(message);
                }
                else
                {
                    // try base64 fallback (in case old double-encoded messages still present)
                    try
                    {
                        var data = System.Convert.FromBase64String(message);
                        var inner = System.Text.Encoding.UTF8.GetString(data);
                        msg = JsonSerializer.Deserialize<ChangeMessage>(inner);
                        _logger.LogInformation("Decoded base64 wrapper for message.");
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize queue message");
                throw; // Re-throw to let Function runtime handle retry logic
            }
            
            if (msg is null)
            {
                _logger.LogWarning("Message skipped (null after deserialization)");
                return;
            }

            var room = msg.RoomUpn;
            var queueDelayMs = msg.ReceivedAtUtc > DateTime.MinValue ? (DateTime.UtcNow - msg.ReceivedAtUtc).TotalMilliseconds : (double?)null;
            _logger.LogInformation("METRIC delta_start room={room} sub={sub} queueDelayMs={delay} recvTs={recv:o}", room, msg.SubscriptionId, queueDelayMs, msg.ReceivedAtUtc);

            var deltaLink = await _state.GetDeltaLinkAsync(room);
            DeltaGetResponse? page;

            try
            {
                if (!string.IsNullOrEmpty(deltaLink))
                {
                    _logger.LogInformation("Using existing delta link for room {room}", room);
                    page = await _graph.Users[room].CalendarView.Delta.WithUrl(deltaLink).GetAsDeltaGetResponseAsync(cfg =>
                    {
                        cfg.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");
                        cfg.Headers.Add("Prefer", "outlook.timezone=\"Tokyo Standard Time\"");
                    });
                }
                else
                {
                    var start = DateTimeOffset.UtcNow.Date.AddDays(-_window.DaysPast);
                    var end = DateTimeOffset.UtcNow.Date.AddDays(_window.DaysFuture);
                    _logger.LogInformation("Starting fresh delta query for room {room} from {start} to {end}", room, start, end);
                    page = await _graph.Users[room].CalendarView.Delta.GetAsDeltaGetResponseAsync(cfg =>
                    {
                        cfg.QueryParameters.StartDateTime = start.ToString("o");
                        cfg.QueryParameters.EndDateTime = end.ToString("o");
                        cfg.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");
                        cfg.Headers.Add("Prefer", "outlook.timezone=\"Tokyo Standard Time\"");
                        // NOTE: $select/$filter/$orderby are not supported on calendarView/delta
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to query Graph API for room {room}", room);
                throw; // Re-throw to let Function runtime handle retry logic
            }

            var loopGuard = 0;
            while (page is not null && loopGuard < 20)
            {
                loopGuard++;
                var events = page.Value ?? new List<Event>();
                _logger.LogInformation("Processing {eventCount} events in delta page {pageNum}", events.Count, loopGuard);
                
                foreach (var ev in events)
                {
                    try
                    {
                        var bodyText = ev.Body?.ContentType == BodyType.Text ? ev.Body?.Content : ev.BodyPreview;
                        var visitorId = _visitor.Extract(bodyText);
                        
                        var bodyPreview = bodyText != null ? bodyText.Substring(0, Math.Min(200, bodyText.Length)) : "null";
                        _logger.LogInformation("Event: {subject} | VisitorID: {visitorId} | BodyText: {bodyText}", 
                            ev.Subject, visitorId ?? "None", bodyPreview);
                        
                        await _cache.UpsertAsync(room, ev, visitorId);

                        // イベント作成→現在までの取り込み遅延 (Graphイベントの CreatedDateTime 利用)
                        double? ingestLatencyMs = null;
                        if (ev.CreatedDateTime != null)
                        {
                            ingestLatencyMs = (DateTimeOffset.UtcNow - ev.CreatedDateTime.Value).TotalMilliseconds;
                        }
                        _logger.LogInformation("METRIC event_cached room={room} eventId={eventId} ingestLatencyMs={ingest} hasVisitor={hasVisitor}", room, ev.Id, ingestLatencyMs, visitorId != null);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process event {eventId} for room {room}", ev.Id, room);
                        // Continue processing other events instead of failing the entire batch
                    }
                }

                if (!string.IsNullOrEmpty(page.OdataNextLink))
                {
                    page = await _graph.Users[room].CalendarView.Delta.WithUrl(page.OdataNextLink).GetAsDeltaGetResponseAsync();
                }
                else
                {
                    if (!string.IsNullOrEmpty(page.OdataDeltaLink))
                        await _state.SetDeltaLinkAsync(room, page.OdataDeltaLink);
                    _logger.LogInformation("No next link. DeltaLink captured? {hasDelta}", !string.IsNullOrEmpty(page.OdataDeltaLink));
                    break;
                }
            }

            _logger.LogInformation("METRIC delta_finished room={room} queueTotalMs={total}", room, msg.ReceivedAtUtc > DateTime.MinValue ? (DateTime.UtcNow - msg.ReceivedAtUtc).TotalMilliseconds : null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeltaWorker failed for message: {message}", message);
            throw; // Re-throw to let Function runtime handle retry logic
        }
    }
}
