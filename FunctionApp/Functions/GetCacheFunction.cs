using System.Net;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FunctionApp.Functions;

public class GetCacheFunction
{
    private readonly ILogger _logger;
    private readonly IConfiguration _config;
    public GetCacheFunction(ILoggerFactory lf, IConfiguration cfg)
    {
        _logger = lf.CreateLogger<GetCacheFunction>();
        _config = cfg;
    }

    [Function("GetCache")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "graph/cache/{roomUpn}/{eventId?}")] HttpRequestData req,
        string roomUpn,
        string? eventId)
    {
        var blobConn = _config["Blob:Connection"] ?? _config["AzureWebJobsStorage"];
        var cacheContainer = _config["Blob:CacheContainer"] ?? "cache";
        var svc = new BlobServiceClient(blobConn);
        var container = svc.GetBlobContainerClient(cacheContainer);

        if (string.IsNullOrEmpty(eventId))
        {
            var entries = new List<object>();
            await foreach (var b in container.GetBlobsAsync(prefix: roomUpn + "/"))
            {
                entries.Add(new { name = b.Name[(roomUpn.Length + 1)..], size = b.Properties.ContentLength, modified = b.Properties.LastModified });
            }
            var respList = req.CreateResponse(HttpStatusCode.OK);
            await respList.WriteStringAsync(JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
            return respList;
        }
        else
        {
            var blob = container.GetBlobClient($"{roomUpn}/{eventId}.json");
            if (!await blob.ExistsAsync())
            {
                var notFound = req.CreateResponse(HttpStatusCode.NotFound);
                await notFound.WriteStringAsync("Event cache not found");
                return notFound;
            }
            var content = await blob.DownloadContentAsync();
            var resp = req.CreateResponse(HttpStatusCode.OK);
            await resp.WriteStringAsync(content.Value.Content.ToString());
            return resp;
        }
    }
}
