using EconomyService.Domain.Enums;

namespace EconomyService.Domain;

public sealed class Currency
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string DisplayName { get; set; }
    public required CurrencyScope Scope { get; init; }
    public Guid? GameId { get; init; }
    public required short Decimals { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? IconUrl { get; set; }
}
