using EconomyService.Domain;

namespace EconomyService.Services;

public sealed record LedgerMutationRequest(
    Guid UserId,
    Guid CurrencyId,
    decimal Amount,
    string IdempotencyKey,
    string? Reason = null);

public sealed record LedgerPostResult(LedgerEntry Entry, decimal Balance, bool IsReplay);

public interface ILedgerService
{
    Task<LedgerPostResult> GrantAsync(LedgerMutationRequest request, CancellationToken cancellationToken = default);

    Task<LedgerPostResult> SpendAsync(LedgerMutationRequest request, CancellationToken cancellationToken = default);
}
