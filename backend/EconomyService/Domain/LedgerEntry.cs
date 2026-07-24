using EconomyService.Domain.Enums;

namespace EconomyService.Domain;

public sealed class LedgerEntry
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public required Guid CurrencyId { get; init; }
    public required decimal Amount { get; init; }
    public required TransactionType TransactionType { get; init; }
    public string? IdempotencyKey { get; init; }
    public string? Reason { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    public Currency? Currency { get; init; }
}
