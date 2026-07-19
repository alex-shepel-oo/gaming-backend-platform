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

    // Amount is a signed delta rather than a magnitude - positive corrects a
    // balance up, negative corrects it down - since an admin correction has
    // no fixed direction the way grant/spend do.
    Task<LedgerPostResult> AdjustAsync(LedgerMutationRequest request, CancellationToken cancellationToken = default);
}
