using System.Net;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FunctionApp.Functions;

public class ClientRenderLatencyAggregatorFunction
{
    private readonly ILogger<ClientRenderLatencyAggregatorFunction> _logger;
    private readonly BlobContainerClient _metricsContainer;

    public ClientRenderLatencyAggregatorFunction(ILogger<ClientRenderLatencyAggregatorFunction> logger)
    {
        _logger = logger;
        var conn = Environment.GetEnvironmentVariable("Blob:Connection") ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage")!;
        var svc = new BlobServiceClient(conn);
        _metricsContainer = svc.GetBlobContainerClient("metrics");
        _metricsContainer.CreateIfNotExists();
    }

    [Function("GetClientRenderLatencyMetrics")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "metrics/client-render-latency")] HttpRequestData req)
    {
        var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var pastMinutes = int.TryParse(qs.Get("pastMinutes"), out var pm) ? pm : 60;
        var cutoff = DateTime.UtcNow.AddMinutes(-pastMinutes);
        var now = DateTime.UtcNow;

        var endToEnd = new List<double>();
        var serverIngest = new List<double>();
        var fetchToRender = new List<double>();

        // 現在日付 + 直前日 (クロス日付をカバー)
        var dates = new[] { now.ToString("yyyyMMdd"), now.AddDays(-1).ToString("yyyyMMdd") };
        foreach (var d in dates)
        {
            await foreach (var blobItem in _metricsContainer.GetBlobsAsync(prefix: $"client/{d}/"))
            {
                try
                {
                    var blob = _metricsContainer.GetBlobClient(blobItem.Name);
                    var content = await blob.DownloadContentAsync();
                    using var doc = JsonDocument.Parse(content.Value.Content.ToStream());
                    var root = doc.RootElement;
                    if (root.TryGetProperty("samples", out var samples) && samples.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var s in samples.EnumerateArray())
                        {
                            if (!s.TryGetProperty("recordedAt", out var recEl) || recEl.ValueKind != JsonValueKind.String) continue;
                            if (!DateTime.TryParse(recEl.GetString(), out var recordedAt)) continue;
                            if (recordedAt < cutoff) continue;
                            AddIf(s, "endToEndMs", endToEnd);
                            AddIf(s, "serverIngestMs", serverIngest);
                            AddIf(s, "fetchToRenderMs", fetchToRender);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to aggregate client metrics blob {blob}", blobItem.Name);
                }
            }
        }

        var result = new
        {
            windowMinutes = pastMinutes,
            collectedAt = now,
            samples = new
            {
                endToEnd = Stats(endToEnd),
                serverIngest = Stats(serverIngest),
                fetchToRender = Stats(fetchToRender)
            },
            targetP95Ms = 10000,
            pass = double.IsNaN(Percentile(endToEnd,95)) ? (bool?)null : Percentile(endToEnd,95) <= 10000
        };

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        await response.WriteStringAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return response;
    }

    private static void AddIf(JsonElement el, string name, List<double> list)
    {
        if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d))
        {
            if (d >= 0) list.Add(d);
        }
    }

    private static object Stats(List<double> values) => values.Count == 0 ? new { count = 0 } : new
    {
        count = values.Count,
        p50 = Percentile(values,50),
        p90 = Percentile(values,90),
        p95 = Percentile(values,95),
        p99 = Percentile(values,99),
        max = values.Max()
    };

    private static double Percentile(List<double> values, int p)
    {
        if (values.Count == 0) return double.NaN;
        var ordered = values.OrderBy(v => v).ToList();
        var rank = (p / 100.0) * (ordered.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        if (low == high) return ordered[low];
        var frac = rank - low;
        return ordered[low] + (ordered[high] - ordered[low]) * frac;
    }
}
