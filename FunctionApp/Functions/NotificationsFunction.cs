
using System.Net;
using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using FunctionApp.Models;
using FunctionApp.Utils;
using FunctionApp.Services;

namespace FunctionApp.Functions;

public class NotificationsFunction
{
    private readonly ILogger _logger;
    private readonly IConfiguration _config;
    private readonly IStateStore _state;

    public NotificationsFunction(ILoggerFactory lf, IConfiguration config, IStateStore state)
    {
        _logger = lf.CreateLogger<NotificationsFunction>();
        _config = config;
        _state = state;
    }

    [Function("Notifications")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "graph/notifications")] HttpRequestData req)
    {
        // URL validation (GET)
        var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var validationToken = qs.Get("validationToken");
        if (!string.IsNullOrEmpty(validationToken))
        {
            _logger.LogInformation("Validation request received. RawUrl={url} tokenLength={len}", req.Url, validationToken.Length);
            var resp = req.CreateResponse(HttpStatusCode.OK);
            resp.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            await resp.WriteStringAsync(validationToken);
            return resp;
        }

        // Change notification (POST)
        var body = await new StreamReader(req.Body).ReadToEndAsync();
        _logger.LogInformation("Notification received: {payload}", body);

        var envelope = JsonSerializer.Deserialize<GraphChangeNotificationEnvelope>(body, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var expectedClientState = _config["Webhook:ClientState"];
        var queueName = _config["Webhook:NotificationQueue"] ?? "graph-notifications";
        var storageConn = _config["AzureWebJobsStorage"]!;
        var queue = new QueueClient(storageConn, queueName);
        await queue.CreateIfNotExistsAsync();

        if (envelope?.Value is null || envelope.Value.Length == 0)
        {
            var resp = req.CreateResponse(HttpStatusCode.Accepted);
            return resp;
        }

        foreach (var n in envelope.Value)
        {
            if (!string.IsNullOrEmpty(expectedClientState) && !string.Equals(n.ClientState, expectedClientState, StringComparison.Ordinal))
            {
                _logger.LogWarning("clientState mismatch for subscription {sub}", n.SubscriptionId);
                continue; // drop suspicious notification
            }

            // First try to extract room from resource
            var room = GraphHelpers.TryParseRoomFromResource(n.Resource);
            
            // If resource-based extraction failed or returned GUID, try subscription lookup
            if (string.IsNullOrEmpty(room) || Guid.TryParse(room, out _))
            {
                _logger.LogInformation("Resource extraction failed or returned GUID ({room}), trying subscription lookup for {sub}", room, n.SubscriptionId);
                room = await _state.GetRoomBySubscriptionIdAsync(n.SubscriptionId);
                
                if (string.IsNullOrEmpty(room))
                {
                    _logger.LogWarning("Could not determine room for subscription {sub}, resource {resource}", n.SubscriptionId, n.Resource);
                    room = "unknown@unknown";
                }
                else
                {
                    _logger.LogInformation("Successfully resolved subscription {sub} to room {room}", n.SubscriptionId, room);
                }
            }

            var msg = new ChangeMessage(
                n.SubscriptionId,
                room,
                n.ChangeType,
                n.Resource,
                DateTime.UtcNow
            );
            var json = JsonSerializer.Serialize(msg);
            // NOTE: QueueClient handles base64 transparently; provide plain JSON
            await queue.SendMessageAsync(json);
            _logger.LogInformation("METRIC enqueue room={room} sub={sub} ts={ts:o}", room, n.SubscriptionId, msg.ReceivedAtUtc);
        }

        // Respond quickly to avoid retries
        return req.CreateResponse(HttpStatusCode.Accepted);
    }
}
