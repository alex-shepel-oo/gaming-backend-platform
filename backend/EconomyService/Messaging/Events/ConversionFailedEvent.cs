using BuildingBlocks.Messaging;

namespace EconomyService.Messaging.Events;

public sealed record ConversionFailedEvent : IntegrationEvent
{
    public required Guid ConversionRequestId { get; init; }
    public required Guid UserId { get; init; }
    public required Guid FromCurrencyId { get; init; }
    public required decimal FromAmount { get; init; }
    public required string Reason { get; init; }
    // Null when the conversion was cancelled before any debit was ever
    // posted (Started -> Failed) - there is no ledger entry to reference in
    // that case, since nothing was ever debited that needed compensating.
    public required Guid? CompensatingLedgerEntryId { get; init; }

    public override string Type => "conversion.failed";
    public override int Version => 1;
}
