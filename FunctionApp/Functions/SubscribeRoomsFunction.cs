
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using FunctionApp.Services;

namespace FunctionApp.Functions;

public class SubscribeRoomsFunction
{
    private readonly ILogger _logger;
    private readonly IConfiguration _config;
    private readonly GraphServiceClient _graph;
    private readonly IStateStore _state;

    public SubscribeRoomsFunction(ILoggerFactory lf, IConfiguration config, GraphServiceClient graph, IStateStore state)
    {
        _logger = lf.CreateLogger<SubscribeRoomsFunction>();
        _config = config;
        _graph = graph;
        _state = state;
    }

    [Function("SubscribeRooms")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "graph/subscribe")] HttpRequestData req)
    {
        var roomsCsv = _config["Rooms:Upns"] ?? "";
        var rooms = roomsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var baseUrl = _config["Webhook:BaseUrl"]?.TrimEnd('/');
        var notificationUrl = $"{baseUrl}/api/graph/notifications";
        var lifecycleUrl = $"{baseUrl}/api/graph/lifecycle"; // optional (not implemented in PoC)

        var clientState = _config["Webhook:ClientState"]!;
        var expires = DateTimeOffset.UtcNow.AddDays(6); // keep margin before 7 days

        var results = new List<object>();

        foreach (var room in rooms)
        {
            var sub = new Subscription
            {
                ChangeType = "created,updated,deleted",
                NotificationUrl = notificationUrl,
                LifecycleNotificationUrl = lifecycleUrl,
                Resource = $"/users/{room}/events",
                ClientState = clientState,
                ExpirationDateTime = expires
            };

            var created = await _graph.Subscriptions.PostAsync(sub);
            if (created is null) continue;

            await _state.SetSubscriptionAsync(new SubscriptionState
            {
                RoomUpn = room,
                SubscriptionId = created.Id!,
                Expiration = created.ExpirationDateTime!.Value,
                ClientStateHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientState)))
            });

            results.Add(new { room, subscriptionId = created.Id, created.ExpirationDateTime });
        }

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteStringAsync(JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        return resp;
    }
}
