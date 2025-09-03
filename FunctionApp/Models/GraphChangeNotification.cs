
namespace FunctionApp.Models;

public record GraphChangeNotificationEnvelope(GraphChangeNotification[] Value);

public record GraphChangeNotification(
    string Id,
    string SubscriptionId,
    string TenantId,
    string? ClientState,
    string ChangeType,
    string Resource,
    DateTimeOffset SubscriptionExpirationDateTime
);
