
namespace FunctionApp.Services;

public class SubscriptionState
{
    public string RoomUpn { get; set; } = default!;
    public string SubscriptionId { get; set; } = default!;
    public DateTimeOffset Expiration { get; set; }
    public string ClientStateHash { get; set; } = default!; // Store hash, not plain
    public string? DeltaLink { get; set; }
}

public interface IStateStore
{
    Task<SubscriptionState?> GetSubscriptionAsync(string roomUpn, CancellationToken ct = default);
    Task SetSubscriptionAsync(SubscriptionState state, CancellationToken ct = default);
    Task<string?> GetDeltaLinkAsync(string roomUpn, CancellationToken ct = default);
    Task SetDeltaLinkAsync(string roomUpn, string deltaLink, CancellationToken ct = default);
    Task<IEnumerable<string>> GetKnownRoomsAsync(CancellationToken ct = default);
    Task<string?> GetRoomBySubscriptionIdAsync(string subscriptionId, CancellationToken ct = default);
}
