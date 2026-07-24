using System.ComponentModel.DataAnnotations;

namespace EconomyService.Contracts.Requests;

public sealed record SpendRequest(
    Guid CurrencyId,
    [property: Range(0.01, double.MaxValue)] decimal Amount,
    string? Reason);
