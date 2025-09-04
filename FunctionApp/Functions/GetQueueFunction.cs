using System.Net;
using Azure.Storage.Queues;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FunctionApp.Functions;

public class GetQueueFunction
{
    private readonly ILogger _logger;
    private readonly IConfiguration _config;
    public GetQueueFunction(ILoggerFactory lf, IConfiguration cfg)
    {
        _logger = lf.CreateLogger<GetQueueFunction>();
        _config = cfg;
    }

    [Function("GetQueue")] 
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "graph/queue")] HttpRequestData req)
    {
        var storage = _config["AzureWebJobsStorage"];
        var queueName = _config["Webhook:NotificationQueue"];
        var queryCollection = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var poison = queryCollection["poison"] == "true";
        
        if (string.IsNullOrEmpty(storage) || string.IsNullOrEmpty(queueName))
        {
            var bad = req.CreateResponse(HttpStatusCode.InternalServerError);
            await bad.WriteStringAsync("Missing AzureWebJobsStorage or Webhook:NotificationQueue");
            return bad;
        }
        
        var actualQueueName = poison ? queueName + "-poison" : queueName;
        var client = new QueueClient(storage, actualQueueName);
        try { await client.CreateIfNotExistsAsync(); } catch { /* ignore */ }
        var peek = await client.PeekMessagesAsync(maxMessages: 16);
        var items = peek.Value.Select(m => new { m.MessageId, m.InsertedOn, m.ExpiresOn, Text = m.MessageText });
        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteStringAsync(JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
        return resp;
    }
}
