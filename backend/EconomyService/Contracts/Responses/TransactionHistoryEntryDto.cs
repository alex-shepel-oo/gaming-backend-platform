using EconomyService.Domain.Enums;

namespace EconomyService.Contracts.Responses;

public sealed record TransactionHistoryEntryDto(
    Guid Id,
    Guid CurrencyId,
    decimal Amount,
    TransactionType TransactionType,
    string? Reason,
    DateTimeOffset CreatedAt);
