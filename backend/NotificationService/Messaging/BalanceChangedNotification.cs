namespace NotificationService.Messaging;

// This service's own view of the event, not EconomyService's
// BalanceChangedEvent: only the fields the hub push actually needs.
public sealed record BalanceChangedNotification
{
    public required Guid UserId { get; init; }
    public required Guid CurrencyId { get; init; }
    public required decimal Amount { get; init; }
    public required decimal Balance { get; init; }
}
