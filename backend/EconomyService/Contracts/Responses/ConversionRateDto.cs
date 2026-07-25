namespace EconomyService.Contracts.Responses;

public sealed record ConversionRateDto(Guid FromCurrencyId, Guid ToCurrencyId, decimal Rate);
