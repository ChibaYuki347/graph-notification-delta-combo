using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Extensions.Configuration;

namespace FunctionApp.Functions;

public class ListEventsFunction
{
    private readonly ILogger _logger;
    private readonly GraphServiceClient _graph;
    public ListEventsFunction(ILoggerFactory lf, GraphServiceClient graph, IConfiguration cfg)
    {
        _logger = lf.CreateLogger<ListEventsFunction>();
        _graph = graph;
    }

    [Function("ListRoomEvents")]
    public async Task<HttpResponseData> Run([
        HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "graph/debug/events/{roomUpn}")
    ] HttpRequestData req, string roomUpn)
    {
        var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var daysPast = int.TryParse(qs.Get("past"), out var p) ? p : 1;
        var daysFuture = int.TryParse(qs.Get("future"), out var f) ? f : 7;
        var start = DateTimeOffset.UtcNow.Date.AddDays(-daysPast);
        var end = DateTimeOffset.UtcNow.Date.AddDays(daysFuture);
        var events = await _graph.Users[roomUpn].CalendarView.GetAsync(cfg =>
        {
            cfg.QueryParameters.StartDateTime = start.ToString("o");
            cfg.QueryParameters.EndDateTime = end.ToString("o");
            cfg.Headers.Add("Prefer", "outlook.body-content-type=\"text\"");
            cfg.Headers.Add("Prefer", "outlook.timezone=\"UTC\"");
        });
        var list = (events?.Value ?? new List<Microsoft.Graph.Models.Event>())
            .Select(ev => new
            {
                ev.Id,
                ev.Subject,
                ev.Organizer?.EmailAddress?.Address,
                Start = ev.Start?.DateTime,
                End = ev.End?.DateTime,
                ev.CreatedDateTime,
                ev.LastModifiedDateTime
            });
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteAsJsonAsync(list);
        return resp;
    }
}
