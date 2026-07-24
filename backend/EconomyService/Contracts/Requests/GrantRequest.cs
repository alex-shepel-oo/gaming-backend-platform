using System.ComponentModel.DataAnnotations;

namespace EconomyService.Contracts.Requests;

public sealed record GrantRequest(
    Guid UserId,
    Guid CurrencyId,
    [property: Range(0.01, double.MaxValue)] decimal Amount,
    [property: Required, StringLength(500, MinimumLength = 1)] string Reason);
