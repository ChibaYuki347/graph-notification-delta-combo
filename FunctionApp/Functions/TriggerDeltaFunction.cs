using System.Net;
using Azure.Storage.Queues;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FunctionApp.Functions;

public class TriggerDeltaFunction
{
    private readonly ILogger _logger;
    private readonly IConfiguration _config;
    public TriggerDeltaFunction(ILoggerFactory lf, IConfiguration cfg)
    {
        _logger = lf.CreateLogger<TriggerDeltaFunction>();
        _config = cfg;
    }

    private record ChangeMessage(string RoomUpn);

    [Function("TriggerDelta")] 
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "graph/debug/trigger")] HttpRequestData req)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var room = query.Get("room");
        if (string.IsNullOrEmpty(room))
        {
            var bad = req.CreateResponse(HttpStatusCode.BadRequest);
            await bad.WriteStringAsync("Specify ?room=roomUpn");
            return bad;
        }
        var storage = _config["AzureWebJobsStorage"]; var queueName = _config["Webhook:NotificationQueue"];
        if (string.IsNullOrEmpty(storage) || string.IsNullOrEmpty(queueName))
        {
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync("Storage or queue setting missing");
            return err;
        }
        var client = new QueueClient(storage, queueName);
        await client.CreateIfNotExistsAsync();
    var payload = JsonSerializer.Serialize(new ChangeMessage(room));
    // NOTE: QueueClient handles base64 encoding internally. Send plain JSON string.
    await client.SendMessageAsync(payload);
    _logger.LogInformation("Manual delta trigger enqueued for {room}. Payload: {payload}", room, payload);
        var ok = req.CreateResponse(HttpStatusCode.Accepted);
        await ok.WriteStringAsync($"Enqueued delta trigger for {room}");
        return ok;
    }
}
