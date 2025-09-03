
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using FunctionApp.Services;

namespace FunctionApp.Functions;

public class RenewSubscriptionsFunction
{
    private readonly ILogger _logger;
    private readonly IConfiguration _config;
    private readonly GraphServiceClient _graph;
    private readonly IStateStore _state;

    public RenewSubscriptionsFunction(ILoggerFactory lf, IConfiguration config, GraphServiceClient graph, IStateStore state)
    {
        _logger = lf.CreateLogger<RenewSubscriptionsFunction>();
        _config = config;
        _graph = graph;
        _state = state;
    }

    [Function("RenewSubscriptions")]
    public async Task Run([TimerTrigger("%Renew:Cron%")] TimerInfo timer)
    {
        var rooms = await _state.GetKnownRoomsAsync();
        var now = DateTimeOffset.UtcNow;

        foreach (var room in rooms)
        {
            var s = await _state.GetSubscriptionAsync(room);
            if (s is null) continue;

            var remaining = s.Expiration - now;
            if (remaining < TimeSpan.FromHours(24))
            {
                var newExp = now.AddDays(6);
                var updated = await _graph.Subscriptions[s.SubscriptionId].PatchAsync(new Subscription
                {
                    ExpirationDateTime = newExp
                });

                _logger.LogInformation("Renewed subscription for {room} to {exp}", room, newExp);
                s.Expiration = newExp;
                await _state.SetSubscriptionAsync(s);
            }
        }
    }
}
