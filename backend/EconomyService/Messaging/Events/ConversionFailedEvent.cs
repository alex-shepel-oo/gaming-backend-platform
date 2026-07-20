namespace EconomyService.Messaging.Events;

public sealed record ConversionFailedEvent : IntegrationEvent
{
    public required Guid ConversionRequestId { get; init; }
    public required Guid UserId { get; init; }
    public required Guid FromCurrencyId { get; init; }
    public required decimal FromAmount { get; init; }
    public required string Reason { get; init; }
    public required Guid CompensatingLedgerEntryId { get; init; }

    public override string Type => "conversion.failed";
    public override int Version => 1;
}
