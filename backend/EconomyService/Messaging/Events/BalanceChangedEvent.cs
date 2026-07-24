using BuildingBlocks.Messaging;
using EconomyService.Domain.Enums;

namespace EconomyService.Messaging.Events;

public sealed record BalanceChangedEvent : IntegrationEvent
{
    public required Guid LedgerEntryId { get; init; }
    public required Guid UserId { get; init; }
    public required Guid CurrencyId { get; init; }
    public required decimal Amount { get; init; }
    public required decimal Balance { get; init; }
    public required TransactionType TransactionType { get; init; }

    public override string Type => "balance.changed";
    public override int Version => 1;
}
