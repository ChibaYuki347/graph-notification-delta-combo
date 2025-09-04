
using System.Net;
using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using FunctionApp.Models;
using FunctionApp.Utils;

namespace FunctionApp.Functions;

public class NotificationsFunction
{
    private readonly ILogger _logger;
    private readonly IConfiguration _config;

    public NotificationsFunction(ILoggerFactory lf, IConfiguration config)
    {
        _logger = lf.CreateLogger<NotificationsFunction>();
        _config = config;
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

            var room = GraphHelpers.TryParseRoomFromResource(n.Resource) ?? "unknown@unknown";
            var msg = new ChangeMessage(n.SubscriptionId, room, n.ChangeType, n.Resource);
            var json = JsonSerializer.Serialize(msg);
            // NOTE: QueueClient handles base64 transparently; provide plain JSON
            await queue.SendMessageAsync(json);
            _logger.LogInformation("Enqueued change message for room {room} sub {sub}", room, n.SubscriptionId);
        }

        // Respond quickly to avoid retries
        return req.CreateResponse(HttpStatusCode.Accepted);
    }
}
