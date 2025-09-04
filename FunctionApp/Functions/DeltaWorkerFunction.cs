
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
        }
        if (msg is null)
        {
            _logger.LogWarning("Message skipped (null after deserialization)");
            return;
        }

        var room = msg.RoomUpn;
        _logger.LogInformation("Processing change for room: {room}", room);

        var deltaLink = await _state.GetDeltaLinkAsync(room);
    DeltaGetResponse? page;

        if (!string.IsNullOrEmpty(deltaLink))
        {
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
            page = await _graph.Users[room].CalendarView.Delta.GetAsDeltaGetResponseAsync(cfg =>
            {
                cfg.QueryParameters.StartDateTime = start.ToString("o");
                cfg.QueryParameters.EndDateTime = end.ToString("o");
                cfg.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");
                cfg.Headers.Add("Prefer", "outlook.timezone=\"Tokyo Standard Time\"");
                // NOTE: $select/$filter/$orderby are not supported on calendarView/delta
            });
        }

        var loopGuard = 0;
        while (page is not null && loopGuard < 20)
        {
            loopGuard++;
            var events = page.Value ?? new List<Event>();
            foreach (var ev in events)
            {
                var visitorId = _visitor.Extract(ev.Body?.ContentType == BodyType.Text ? ev.Body?.Content : ev.BodyPreview);
                await _cache.UpsertAsync(room, ev, visitorId);
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

        _logger.LogInformation("Delta sync finished: {room}", room);
    }
}
