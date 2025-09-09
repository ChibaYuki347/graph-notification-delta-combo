
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;

using FunctionApp.Services;
using FunctionApp.Utils;
using FunctionApp.Functions;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((ctx, services) =>
    {
        var cfg = ctx.Configuration;

        // Graph auth
        var tenantId = cfg["Graph:TenantId"];
        var clientId = cfg["Graph:ClientId"];
        var clientSecret = cfg["Graph:ClientSecret"];

        TokenCredential credential;
        if (!string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret))
        {
            credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        }
        else
        {
            // Fallback to Managed Identity (recommended in Azure)
            credential = new DefaultAzureCredential();
        }

        services.AddSingleton(new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" }));

        // Blob state/cache
        var blobConn = cfg["Blob:Connection"] ?? cfg["AzureWebJobsStorage"];
        var stateContainer = cfg["Blob:StateContainer"] ?? "state";
        var cacheContainer = cfg["Blob:CacheContainer"] ?? "cache";

        services.AddSingleton<IStateStore>(sp => new BlobStateStore(blobConn!, stateContainer));
        services.AddSingleton<IEventCacheStore>(sp => 
        {
            var logger = sp.GetRequiredService<ILogger<BlobEventCacheStore>>();
            return new BlobEventCacheStore(blobConn!, cacheContainer, logger);
        });
        services.AddSingleton<VisitorIdExtractor>();

        // Options
        services.AddSingleton(new WindowOptions
        {
            DaysPast = int.TryParse(cfg["Window:DaysPast"], out var dp) ? dp : 1,
            DaysFuture = int.TryParse(cfg["Window:DaysFuture"], out var df) ? df : 7
        });
    })
    .Build();

host.Run();
