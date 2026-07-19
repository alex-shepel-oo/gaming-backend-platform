namespace EconomyService.Messaging.Events;

public sealed record ConversionDebitedEvent : IntegrationEvent
{
    public required Guid ConversionRequestId { get; init; }
    public required Guid UserId { get; init; }
    public required Guid FromCurrencyId { get; init; }
    public required decimal FromAmount { get; init; }
    public required Guid LedgerEntryId { get; init; }

    public override string Type => "conversion.debited";
    public override int Version => 1;
}
