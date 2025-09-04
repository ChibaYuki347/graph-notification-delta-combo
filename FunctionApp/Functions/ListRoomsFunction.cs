using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace FunctionApp.Functions;

public class ListRoomsFunction
{
    private readonly ILogger _logger;
    private readonly GraphServiceClient _graph;
    
    public ListRoomsFunction(ILoggerFactory lf, GraphServiceClient graph)
    {
        _logger = lf.CreateLogger<ListRoomsFunction>();
        _graph = graph;
    }

    [Function("ListRooms")]
    public async Task<HttpResponseData> Run([
        HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "graph/debug/rooms")
    ] HttpRequestData req)
    {
        try
        {
            // Search for room-like users directly
            var users = await _graph.Users.GetAsync(cfg =>
            {
                cfg.QueryParameters.Filter = "userType eq 'Member' and accountEnabled eq true";
                cfg.QueryParameters.Select = new[] { "id", "displayName", "userPrincipalName", "mail" };
                cfg.QueryParameters.Search = "\"ConfRoom\"";
                cfg.QueryParameters.Top = 50;
                cfg.Headers.Add("ConsistencyLevel", "eventual");
            });

            var roomList = (users?.Value ?? new List<User>())
                .Where(u => u.UserPrincipalName?.Contains("ConfRoom", StringComparison.OrdinalIgnoreCase) == true ||
                           u.DisplayName?.Contains("ConfRoom", StringComparison.OrdinalIgnoreCase) == true ||
                           u.UserPrincipalName?.Contains("@bbslooklab.onmicrosoft.com", StringComparison.OrdinalIgnoreCase) == true)
                .Select(user => new
                {
                    DisplayName = user.DisplayName,
                    EmailAddress = user.Mail ?? user.UserPrincipalName,
                    UserPrincipalName = user.UserPrincipalName,
                    Id = user.Id
                })
                .OrderBy(r => r.DisplayName)
                .ToList();

            _logger.LogInformation("Found {count} room-like users", roomList.Count);

            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteAsJsonAsync(roomList);
            return resp;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve room users");
            
            // Manual fallback: return known rooms from config
            var knownRooms = new[]
            {
                new { DisplayName = "Conference Room 1", UserPrincipalName = "ConfRoom1@bbslooklab.onmicrosoft.com", EmailAddress = "ConfRoom1@bbslooklab.onmicrosoft.com", Id = "ConfRoom1" },
                new { DisplayName = "Conference Room 2", UserPrincipalName = "ConfRoom2@bbslooklab.onmicrosoft.com", EmailAddress = "ConfRoom2@bbslooklab.onmicrosoft.com", Id = "ConfRoom2" },
                new { DisplayName = "Conference Room 3", UserPrincipalName = "ConfRoom3@bbslooklab.onmicrosoft.com", EmailAddress = "ConfRoom3@bbslooklab.onmicrosoft.com", Id = "ConfRoom3" },
                new { DisplayName = "Conference Room 4", UserPrincipalName = "ConfRoom4@bbslooklab.onmicrosoft.com", EmailAddress = "ConfRoom4@bbslooklab.onmicrosoft.com", Id = "ConfRoom4" },
                new { DisplayName = "Conference Room 5", UserPrincipalName = "ConfRoom5@bbslooklab.onmicrosoft.com", EmailAddress = "ConfRoom5@bbslooklab.onmicrosoft.com", Id = "ConfRoom5" }
            };

            var errorResp = req.CreateResponse(HttpStatusCode.OK);
            await errorResp.WriteAsJsonAsync(new { 
                Note = "Used hardcoded room list due to Graph API search failure",
                Error = ex.Message,
                Rooms = knownRooms 
            });
            return errorResp;
        }
    }
}
