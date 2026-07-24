namespace EconomyService.Domain;

public sealed class ConversionRate
{
    public required Guid Id { get; init; }
    public required Guid FromCurrencyId { get; init; }
    public required Guid ToCurrencyId { get; init; }
    public required decimal Rate { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    public Currency? FromCurrency { get; init; }
    public Currency? ToCurrency { get; init; }
}
