
namespace FunctionApp.Models;

public record ChangeMessage(
    string SubscriptionId,
    string RoomUpn,
    string ChangeType,
    string Resource
);

public record LifecycleMessage(
    string Reason,
    string SubscriptionId,
    string? RoomUpn
);
