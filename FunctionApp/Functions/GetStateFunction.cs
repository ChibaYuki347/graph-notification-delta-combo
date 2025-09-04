using System.Net;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FunctionApp.Functions;

public class GetStateFunction
{
    private readonly ILogger _logger;
    private readonly IConfiguration _config;
    public GetStateFunction(ILoggerFactory lf, IConfiguration cfg)
    {
        _logger = lf.CreateLogger<GetStateFunction>();
        _config = cfg;
    }

    [Function("GetState")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "graph/state/{roomUpn?}")] HttpRequestData req,
        string? roomUpn)
    {
        var blobConn = _config["Blob:Connection"] ?? _config["AzureWebJobsStorage"];
        var stateContainer = _config["Blob:StateContainer"] ?? "state";
        var svc = new BlobServiceClient(blobConn);
        var container = svc.GetBlobContainerClient(stateContainer);

        var list = new List<object>();
        if (string.IsNullOrEmpty(roomUpn))
        {
            await foreach (var b in container.GetBlobsAsync(prefix: "sub/"))
            {
                if (!b.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new { room = b.Name[4..^5], size = b.Properties.ContentLength, modified = b.Properties.LastModified });
            }
            var respAll = req.CreateResponse(HttpStatusCode.OK);
            await respAll.WriteStringAsync(JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
            return respAll;
        }

        // specific room: subscription json + delta link
        var subBlob = container.GetBlobClient($"sub/{roomUpn}.json");
        var deltaBlob = container.GetBlobClient($"sub/{roomUpn}.delta");
        object? sub = null;
        string? delta = null;
        if (await subBlob.ExistsAsync())
        {
            var c = await subBlob.DownloadContentAsync();
            sub = JsonSerializer.Deserialize<object>(c.Value.Content.ToString());
        }
        if (await deltaBlob.ExistsAsync())
        {
            var c = await deltaBlob.DownloadContentAsync();
            delta = c.Value.Content.ToString();
        }

        var resp = req.CreateResponse(HttpStatusCode.OK);
        await resp.WriteStringAsync(JsonSerializer.Serialize(new { room = roomUpn, subscription = sub, deltaLink = delta }, new JsonSerializerOptions { WriteIndented = true }));
        return resp;
    }
}
