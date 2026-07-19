using EconomyService.Domain.Enums;

namespace EconomyService.Contracts.Responses;

public sealed record CurrencyDto(Guid Id, string Code, string DisplayName, CurrencyScope Scope, Guid? GameId);
