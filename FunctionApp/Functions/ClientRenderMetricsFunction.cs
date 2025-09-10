using System.Net;
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FunctionApp.Functions;

public class ClientRenderMetricsFunction
{
    private readonly ILogger<ClientRenderMetricsFunction> _logger;
    private readonly BlobContainerClient _metricsContainer;

    public ClientRenderMetricsFunction(ILogger<ClientRenderMetricsFunction> logger)
    {
        _logger = logger;
        var conn = Environment.GetEnvironmentVariable("Blob:Connection") ?? Environment.GetEnvironmentVariable("AzureWebJobsStorage")!;
        var svc = new BlobServiceClient(conn);
        _metricsContainer = svc.GetBlobContainerClient("metrics");
        _metricsContainer.CreateIfNotExists();
    }

    private record ClientSample(string EventId, string? Room, DateTime? Created, DateTime? IngestedAtUtc, DateTime? FetchedAtUtc, DateTime? RenderAtUtc);
    private record Payload(ClientSample[] Samples);

    [Function("PostClientRenderMetrics")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "metrics/client-render")] HttpRequestData req)
    {
        try
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(body))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteStringAsync("empty body");
                return bad;
            }
            var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("samples", out var samplesEl) || samplesEl.ValueKind != JsonValueKind.Array)
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteStringAsync("missing samples[]");
                return bad;
            }

            var now = DateTime.UtcNow;
            var list = new List<object>();
            foreach (var s in samplesEl.EnumerateArray())
            {
                string eventId = s.GetProperty("eventId").GetString() ?? Guid.NewGuid().ToString("N");
                string? room = s.TryGetProperty("room", out var rEl) && rEl.ValueKind == JsonValueKind.String ? rEl.GetString() : null;
                DateTime? created = ParseDt(s, "created");
                DateTime? ingested = ParseDt(s, "ingestedAtUtc");
                DateTime? fetched = ParseDt(s, "fetchedAtUtc");
                DateTime? render = ParseDt(s, "renderAtUtc");

                double? endToEndMs = (render - created)?.TotalMilliseconds;
                double? serverIngestMs = (ingested - created)?.TotalMilliseconds;
                double? fetchToRenderMs = (render - fetched)?.TotalMilliseconds;

                list.Add(new
                {
                    eventId,
                    room,
                    created,
                    ingestedAtUtc = ingested,
                    fetchedAtUtc = fetched,
                    renderAtUtc = render,
                    endToEndMs,
                    serverIngestMs,
                    fetchToRenderMs,
                    recordedAt = now
                });
            }

            // 保存: 1 リクエスト = 1 blob (日付プレフィックス)
            var prefix = now.ToString("yyyyMMdd");
            var blob = _metricsContainer.GetBlobClient($"client/{prefix}/{Guid.NewGuid():N}.json");
            var payload = JsonSerializer.Serialize(new { timestamp = now, samples = list }, new JsonSerializerOptions { WriteIndented = false });
            await blob.UploadAsync(BinaryData.FromString(payload));

            var ok = req.CreateResponse(HttpStatusCode.Accepted);
            await ok.WriteStringAsync("ok");
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "client render metrics error");
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync(ex.Message);
            return err;
        }
    }

    private static DateTime? ParseDt(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && DateTime.TryParse(v.GetString(), out var dt))
            return dt;
        return null;
    }
}
