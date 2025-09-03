
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
        var msg = JsonSerializer.Deserialize<ChangeMessage>(message);
        if (msg is null) return;

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

        while (page is not null)
        {
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
                break;
            }
        }

        _logger.LogInformation("Delta sync finished: {room}", room);
    }
}
