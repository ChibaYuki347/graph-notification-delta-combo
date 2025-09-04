
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
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "graph/subscribe")] HttpRequestData req)
    {
        // Allow override via ?rooms=roomA,roomB
        var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var roomsCsv = qs.Get("rooms") ?? _config["Rooms:Upns"] ?? "";
        var rooms = roomsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (rooms.Length == 0)
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("No rooms specified.");
            return bad;
        }

        var baseUrl = _config["Webhook:BaseUrl"]?.TrimEnd('/');
        var notificationUrl = $"{baseUrl}/api/graph/notifications";
        if (string.IsNullOrWhiteSpace(baseUrl) || baseUrl.Contains("REPLACE_WITH_PUBLIC_HTTPS", StringComparison.OrdinalIgnoreCase))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync(JsonSerializer.Serialize(new
            {
                error = "InvalidBaseUrl",
                message = "Webhook__BaseUrl is not set to a public https URL (ngrok or deployed Functions). Update local.settings.json and restart.",
                current = baseUrl
            }));
            return bad;
        }

        var clientState = _config["Webhook:ClientState"]!;
        var expires = DateTimeOffset.UtcNow.AddDays(6); // keep margin before 7 days

        var results = new List<object>();

        foreach (var room in rooms)
        {
            try
            {
                var sub = new Subscription
                {
                    ChangeType = "created,updated,deleted",
                    NotificationUrl = notificationUrl,
                    // LifecycleNotificationUrl intentionally omitted for PoC to avoid validation failure
                    Resource = $"/users/{room}/events",
                    ClientState = clientState,
                    ExpirationDateTime = expires
                };

                var created = await _graph.Subscriptions.PostAsync(sub);
                if (created is null)
                {
                    results.Add(new { room, error = "NullSubscriptionReturned" });
                    continue;
                }

                await _state.SetSubscriptionAsync(new SubscriptionState
                {
                    RoomUpn = room,
                    SubscriptionId = created.Id!,
                    Expiration = created.ExpirationDateTime!.Value,
                    ClientStateHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientState)))
                });

                results.Add(new { room, subscriptionId = created.Id, created.ExpirationDateTime });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed creating subscription for {room}", room);
                results.Add(new { room, error = ex.GetType().Name, message = ex.Message });
            }
        }

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteStringAsync(JsonSerializer.Serialize(new
        {
            notificationUrl,
            count = results.Count,
            results
        }, new JsonSerializerOptions { WriteIndented = true }));
        return resp;
    }
}
