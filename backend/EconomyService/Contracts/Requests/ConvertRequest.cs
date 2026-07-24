using System.ComponentModel.DataAnnotations;

namespace EconomyService.Contracts.Requests;

public sealed record ConvertRequest(
    Guid FromCurrencyId,
    Guid ToCurrencyId,
    [property: Range(0.01, double.MaxValue)] decimal FromAmount);
