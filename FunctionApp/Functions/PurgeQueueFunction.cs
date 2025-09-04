using System.Net;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FunctionApp.Functions;

public class PurgeQueueFunction
{
    private readonly ILogger _logger;
    private readonly IConfiguration _config;
    public PurgeQueueFunction(ILoggerFactory lf, IConfiguration cfg)
    {
        _logger = lf.CreateLogger<PurgeQueueFunction>();
        _config = cfg;
    }

    [Function("PurgeQueue")]
    public async Task<HttpResponseData> Run([
        HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "graph/queue/purge")
    ] HttpRequestData req)
    {
        var storage = _config["AzureWebJobsStorage"]; var queueName = _config["Webhook:NotificationQueue"];
        if (string.IsNullOrEmpty(storage) || string.IsNullOrEmpty(queueName))
        {
            var bad = req.CreateResponse(HttpStatusCode.InternalServerError);
            await bad.WriteStringAsync("Missing storage or queue setting");
            return bad;
        }
        var client = new QueueClient(storage, queueName);
        await client.CreateIfNotExistsAsync();
        await client.ClearMessagesAsync();
        _logger.LogWarning("Queue {queue} purged", queueName);
        var ok = req.CreateResponse(HttpStatusCode.OK);
        await ok.WriteStringAsync("Purged");
        return ok;
    }
}
