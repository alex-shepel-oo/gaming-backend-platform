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

    // The three conversion-step postings below all take onPosted, a callback
    // run inside the same transaction as the ledger entry and balance update,
    // right before commit. The conversion saga uses it to advance
    // conversion_requests.status and write its own outbox event alongside the
    // posting, instead of reimplementing the entry/balance/outbox mechanics
    // that already live in this service.
    Task<LedgerPostResult> ConversionDebitAsync(
        LedgerMutationRequest request,
        Func<LedgerPostResult, CancellationToken, Task> onPosted,
        CancellationToken cancellationToken = default);

    Task<LedgerPostResult> ConversionCreditAsync(
        LedgerMutationRequest request,
        Func<LedgerPostResult, CancellationToken, Task> onPosted,
        CancellationToken cancellationToken = default);

    Task<LedgerPostResult> ConversionCompensateAsync(
        LedgerMutationRequest request,
        Func<LedgerPostResult, CancellationToken, Task> onPosted,
        CancellationToken cancellationToken = default);
}
