using EconomyService.Options;
using EconomyService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EconomyService.Services;

// Thin wrapper over ILedgerService.GrantAsync: resolves the configured
// currency by code and posts a grant under a per-user idempotency key. All
// the actual ledger/balance/outbox mechanics already live in LedgerService -
// this adds nothing beyond the currency lookup and the key it grants under.
public sealed class WelcomeGrantService(
    EconomyDbContext dbContext,
    ILedgerService ledgerService,
    IOptions<WelcomeGrantOptions> options) : IWelcomeGrantService
{
    public async Task GrantAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var currency = await dbContext.Currencies
            .SingleAsync(c => c.Code == options.Value.CurrencyCode, cancellationToken);

        await ledgerService.GrantAsync(
            new LedgerMutationRequest(userId, currency.Id, options.Value.Amount, $"welcome:{userId}", "Welcome grant"),
            cancellationToken);
    }
}
