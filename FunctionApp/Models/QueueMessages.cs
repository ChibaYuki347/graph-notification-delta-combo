
namespace FunctionApp.Models;

public record ChangeMessage(
    string SubscriptionId,
    string RoomUpn,
    string ChangeType,
    string Resource,
    DateTime ReceivedAtUtc // Webhook受信→キュー投入時刻 (後方互換: 旧メッセージは MinValue)
);

public record LifecycleMessage(
    string Reason,
    string SubscriptionId,
    string? RoomUpn
);
