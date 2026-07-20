namespace EconomyService.Messaging.Events;

public sealed record ConversionCompletedEvent : IntegrationEvent
{
    public required Guid ConversionRequestId { get; init; }
    public required Guid UserId { get; init; }
    public required Guid ToCurrencyId { get; init; }
    public required decimal ToAmount { get; init; }
    public required Guid LedgerEntryId { get; init; }

    public override string Type => "conversion.completed";
    public override int Version => 1;
}
