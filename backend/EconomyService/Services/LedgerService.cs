using EconomyService.Domain;
using EconomyService.Domain.Enums;
using EconomyService.Exceptions;
using EconomyService.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EconomyService.Services;

public sealed class LedgerService(
    EconomyDbContext dbContext,
    IIdempotencyStore idempotencyStore,
    IBalanceService balanceService,
    TimeProvider timeProvider) : ILedgerService
{
    // Bounds the optimistic-concurrency retry loop below: a losing writer
    // reloads the balance and tries again rather than failing outright, so
    // this is a safety cap against pathological contention, not the
    // expected path.
    private const int MaxVersionRetries = 10;

    public Task<LedgerPostResult> GrantAsync(LedgerMutationRequest request, CancellationToken cancellationToken = default) =>
        PostAsync(request, TransactionType.Grant, request.Amount, cancellationToken);

    public Task<LedgerPostResult> SpendAsync(LedgerMutationRequest request, CancellationToken cancellationToken = default) =>
        PostAsync(request, TransactionType.Spend, -request.Amount, cancellationToken);

    private async Task<LedgerPostResult> PostAsync(
        LedgerMutationRequest request,
        TransactionType transactionType,
        decimal signedAmount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);

        var existingEntry = await idempotencyStore.FindExistingAsync(request.IdempotencyKey, cancellationToken);
        if (existingEntry is not null)
        {
            return await ReplayAsync(existingEntry, request, cancellationToken);
        }

        for (var attempt = 1; attempt <= MaxVersionRetries; attempt++)
        {
            var balance = await GetOrCreateBalanceAsync(request.UserId, request.CurrencyId, cancellationToken);
            var newAmount = balance.Amount + signedAmount;

            if (newAmount < 0)
            {
                throw new InsufficientFundsException();
            }

            var now = timeProvider.GetUtcNow();
            var entry = new LedgerEntry
            {
                Id = Guid.CreateVersion7(),
                UserId = request.UserId,
                CurrencyId = request.CurrencyId,
                Amount = signedAmount,
                TransactionType = transactionType,
                IdempotencyKey = request.IdempotencyKey,
                Reason = request.Reason,
                CreatedAt = now,
            };

            // Ledger insert and balance projection update commit together or
            // not at all - a crash between them must never leave a posted
            // spend that never touched the balance it drew from.
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            dbContext.LedgerEntries.Add(entry);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();

                // Another request raced us with the same idempotency key and
                // won; replay its outcome instead of surfacing a duplicate
                // key error to a caller who did nothing wrong.
                var racedEntry = await idempotencyStore.FindExistingAsync(request.IdempotencyKey, cancellationToken);
                if (racedEntry is null)
                {
                    throw;
                }

                return await ReplayAsync(racedEntry, request, cancellationToken);
            }

            var affectedRows = await dbContext.Balances
                .Where(b => b.Id == balance.Id && b.Version == balance.Version)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(b => b.Amount, newAmount)
                        .SetProperty(b => b.Version, b => b.Version + 1)
                        .SetProperty(b => b.UpdatedAt, now),
                    cancellationToken);

            if (affectedRows != 1)
            {
                // Someone else posted against this balance between our read
                // and our write. Roll back and retry against a fresh read
                // rather than losing this mutation silently.
                await transaction.RollbackAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
                continue;
            }

            await transaction.CommitAsync(cancellationToken);
            return new LedgerPostResult(entry, newAmount, IsReplay: false);
        }

        throw new BalanceConcurrencyException();
    }

    private async Task<LedgerPostResult> ReplayAsync(
        LedgerEntry existingEntry, LedgerMutationRequest request, CancellationToken cancellationToken)
    {
        var balance = await balanceService.GetBalanceAsync(request.UserId, request.CurrencyId, cancellationToken);
        return new LedgerPostResult(existingEntry, balance, IsReplay: true);
    }

    private async Task<Balance> GetOrCreateBalanceAsync(Guid userId, Guid currencyId, CancellationToken cancellationToken)
    {
        var balance = await dbContext.Balances
            .AsNoTracking()
            .SingleOrDefaultAsync(b => b.UserId == userId && b.CurrencyId == currencyId, cancellationToken);

        if (balance is not null)
        {
            return balance;
        }

        var newBalance = new Balance
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            CurrencyId = currencyId,
            Amount = 0m,
            Version = 0,
            UpdatedAt = timeProvider.GetUtcNow(),
        };

        dbContext.Balances.Add(newBalance);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another request provisioned the same (user, currency) balance
            // concurrently - the unique index caught it, read back what it wrote.
            dbContext.ChangeTracker.Clear();

            return await dbContext.Balances
                .AsNoTracking()
                .SingleAsync(b => b.UserId == userId && b.CurrencyId == currencyId, cancellationToken);
        }

        dbContext.Entry(newBalance).State = EntityState.Detached;
        return newBalance;
    }
}
