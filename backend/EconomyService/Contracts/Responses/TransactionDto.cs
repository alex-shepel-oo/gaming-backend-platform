using EconomyService.Domain.Enums;

namespace EconomyService.Contracts.Responses;

public sealed record TransactionDto(
    Guid Id,
    Guid UserId,
    Guid CurrencyId,
    decimal Amount,
    TransactionType TransactionType,
    string? Reason,
    decimal Balance,
    DateTimeOffset CreatedAt);
