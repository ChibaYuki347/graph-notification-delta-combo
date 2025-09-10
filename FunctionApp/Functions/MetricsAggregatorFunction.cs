using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using FunctionApp.Services;

namespace FunctionApp.Functions;

public class MetricsAggregatorFunction
{
    private readonly ILogger<MetricsAggregatorFunction> _logger;
    private readonly IEventCacheStore _cache;

    public MetricsAggregatorFunction(ILogger<MetricsAggregatorFunction> logger, IEventCacheStore cache)
    {
        _logger = logger;
        _cache = cache;
    }

    // GET /api/metrics/latency?room=ConfRoom1@...&pastMinutes=30
    [Function("GetLatencyMetrics")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "metrics/latency")] HttpRequestData req)
    {
        var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var room = qs.Get("room"); // null なら全室集計
        var pastMinutes = int.TryParse(qs.Get("pastMinutes"), out var pm) ? pm : 60;
        var cutoff = DateTime.UtcNow.AddMinutes(-pastMinutes);

        var rooms = new List<string>();
        if (!string.IsNullOrEmpty(room))
        {
            rooms.Add(room);
        }
        else
        {
            // 既存 Blob 走査 (prefix 取得) は SDK 直接では難しいため、既知ルームを呼び出し側で指定する想定。
            // 簡易: ConfRoom1..16 を対象
            rooms.AddRange(Enumerable.Range(1,16).Select(i => $"ConfRoom{i}@bbslooklab.onmicrosoft.com"));
        }

        var perRoom = new List<object>();
        var globalLatencies = new List<double>();

        foreach (var r in rooms)
        {
            try
            {
                var events = await _cache.GetAllEventsAsync(r);
                var latencies = new List<double>();
                foreach (var e in events)
                {
                    if (!e.TryGetProperty("created", out var createdEl) || createdEl.ValueKind != JsonValueKind.String) continue;
                    if (!e.TryGetProperty("ingestedAtUtc", out var ingEl) || ingEl.ValueKind != JsonValueKind.String) continue;
                    if (!DateTime.TryParse(createdEl.GetString(), out var created)) continue;
                    if (!DateTime.TryParse(ingEl.GetString(), out var ingested)) continue;
                    if (ingested < cutoff) continue; // 古い
                    var latencyMs = (ingested - created).TotalMilliseconds;
                    if (latencyMs < 0) continue;
                    latencies.Add(latencyMs);
                }
                if (latencies.Count > 0)
                {
                    globalLatencies.AddRange(latencies);
                    perRoom.Add(new
                    {
                        room = r,
                        count = latencies.Count,
                        p50 = Percentile(latencies, 50),
                        p90 = Percentile(latencies, 90),
                        p95 = Percentile(latencies, 95),
                        p99 = Percentile(latencies, 99),
                        max = latencies.Max()
                    });
                }
                else
                {
                    perRoom.Add(new { room = r, count = 0 });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Latency aggregation failed for room {room}", r);
                perRoom.Add(new { room = r, error = ex.Message });
            }
        }

        var overall = new
        {
            windowMinutes = pastMinutes,
            roomsEvaluated = rooms.Count,
            totalSamples = globalLatencies.Count,
            overall = globalLatencies.Count == 0 ? null : new
            {
                p50 = Percentile(globalLatencies, 50),
                p90 = Percentile(globalLatencies, 90),
                p95 = Percentile(globalLatencies, 95),
                p99 = Percentile(globalLatencies, 99),
                max = globalLatencies.Max()
            },
            perRoom = perRoom,
            targetP95Ms = 10000,
            pass = globalLatencies.Count == 0 ? (bool?)null : Percentile(globalLatencies, 95) <= 10000
        };

        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Access-Control-Allow-Origin", "*");
        await resp.WriteStringAsync(JsonSerializer.Serialize(overall, new JsonSerializerOptions { WriteIndented = true }));
        return resp;
    }

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
