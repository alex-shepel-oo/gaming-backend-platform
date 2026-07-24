using EconomyService.Domain.Enums;

namespace EconomyService.Domain;

public sealed class ConversionRequest
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public required Guid FromCurrencyId { get; init; }
    public required Guid ToCurrencyId { get; init; }
    public Guid? GameId { get; init; }
    public required decimal FromAmount { get; init; }
    public required decimal ToAmount { get; init; }
    public required decimal RateApplied { get; init; }
    public required ConversionStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; set; }

    public Currency? FromCurrency { get; init; }
    public Currency? ToCurrency { get; init; }
}
