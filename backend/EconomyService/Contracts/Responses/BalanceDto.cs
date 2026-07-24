using EconomyService.Domain.Enums;

namespace EconomyService.Contracts.Responses;

public sealed record BalanceDto(Guid CurrencyId, string CurrencyCode, CurrencyScope Scope, Guid? GameId, decimal Amount);
