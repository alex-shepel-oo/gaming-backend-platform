namespace EconomyService.Domain;

public sealed class Balance
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public required Guid CurrencyId { get; init; }
    public required decimal Amount { get; set; }
    public required long Version { get; set; }
    public required DateTimeOffset UpdatedAt { get; set; }

    public Currency? Currency { get; init; }
}
