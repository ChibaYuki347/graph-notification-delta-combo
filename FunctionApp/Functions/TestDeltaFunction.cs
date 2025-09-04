using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.CalendarView.Delta;
using FunctionApp.Services;
using FunctionApp.Functions;
using FunctionApp.Utils;

namespace FunctionApp.Functions;

public class TestDeltaFunction
{
    private readonly ILogger _logger;
    private readonly GraphServiceClient _graph;
    private readonly IStateStore _state;
    private readonly IEventCacheStore _cache;
    private readonly VisitorIdExtractor _visitor;
    private readonly WindowOptions _window;

    public TestDeltaFunction(ILoggerFactory lf, GraphServiceClient graph, IStateStore state, IEventCacheStore cache, VisitorIdExtractor visitor, WindowOptions window)
    {
        _logger = lf.CreateLogger<TestDeltaFunction>();
        _graph = graph;
        _state = state;
        _cache = cache;
        _visitor = visitor;
        _window = window;
    }

    [Function("TestDelta")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "graph/debug/test-delta")] HttpRequestData req)
    {
        var queryCollection = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var room = queryCollection["room"] ?? "ConfRoom1@bbslooklab.onmicrosoft.com";

        _logger.LogInformation("TestDelta starting for room: {room}", room);
        
        try
        {
            var deltaLink = await _state.GetDeltaLinkAsync(room);
            DeltaGetResponse? page;

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
                });
            }

            var loopGuard = 0;
            var totalEvents = 0;
            while (page is not null && loopGuard < 20)
            {
                loopGuard++;
                var events = page.Value ?? new List<Event>();
                totalEvents += events.Count;
                _logger.LogInformation("Processing {eventCount} events in delta page {pageNum}", events.Count, loopGuard);
                
                foreach (var ev in events)
                {
                    var bodyText = ev.Body?.ContentType == BodyType.Text ? ev.Body?.Content : ev.BodyPreview;
                    var visitorId = _visitor.Extract(bodyText);
                    
                    var bodyPreview = bodyText != null ? bodyText.Substring(0, Math.Min(200, bodyText.Length)) : "null";
                    _logger.LogInformation("Event: {subject} | VisitorID: {visitorId} | BodyText: {bodyText}", 
                        ev.Subject, visitorId ?? "None", bodyPreview);
                    
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

            _logger.LogInformation("TestDelta finished: {room}, processed {totalEvents} total events", room, totalEvents);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync($"Delta test completed for {room}. Processed {totalEvents} events in {loopGuard} pages.");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TestDelta failed for room {room}", room);
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync($"Error: {ex.Message}");
            return errorResponse;
        }
    }
}
