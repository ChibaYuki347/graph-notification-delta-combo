using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FunctionApp.Functions;

public class WarmupFunction
{
    private readonly ILogger _logger;

    public WarmupFunction(ILoggerFactory lf)
    {
        _logger = lf.CreateLogger<WarmupFunction>();
    }

    // 軽量ウォームアップ / 可用性確認用。Graph サブスクリプション登録前に叩くとコールドスタート遅延を低減。
    [Function("Warmup")]
    public HttpResponseData Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "warmup")] HttpRequestData req)
    {
        if (req.Url.Query.Contains("validationToken"))
        {
            // 念のため validationToken を返しても害は無い (Graph は notifications エンドポイントを使う想定)
            var qs = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var token = qs.Get("validationToken");
            var r = req.CreateResponse(HttpStatusCode.OK);
            r.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            r.WriteString(token ?? "ok");
            return r;
        }

        _logger.LogInformation("Warmup ping {path}", req.Url.AbsolutePath);
        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.Headers.Add("Content-Type", "text/plain; charset=utf-8");
        resp.WriteString("ok");
        return resp;
    }
}
