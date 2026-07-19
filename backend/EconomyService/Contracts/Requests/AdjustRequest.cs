using System.ComponentModel.DataAnnotations;

namespace EconomyService.Contracts.Requests;

public sealed record AdjustRequest(
    Guid CurrencyId,
    decimal Amount,
    [property: Required, StringLength(500, MinimumLength = 1)] string Reason);
