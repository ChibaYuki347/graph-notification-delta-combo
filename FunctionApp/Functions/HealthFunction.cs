using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace FunctionApp.Functions;

public class HealthFunction
{
    private readonly ILogger _logger;
    public HealthFunction(ILoggerFactory lf) => _logger = lf.CreateLogger<HealthFunction>();

    [Function("Health")]
    public HttpResponseData Run([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        var resp = req.CreateResponse(HttpStatusCode.OK);
        resp.WriteString($"OK {DateTime.UtcNow:o}");
        return resp;
    }
}
