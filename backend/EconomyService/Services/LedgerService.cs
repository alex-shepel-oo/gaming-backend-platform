using EconomyService.Domain;
using EconomyService.Domain.Enums;
using EconomyService.Exceptions;
using EconomyService.Messaging.Events;
using EconomyService.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EconomyService.Services;

public sealed class LedgerService(
    EconomyDbContext dbContext,
    IIdempotencyStore idempotencyStore,
    IBalanceService balanceService,
    IOutboxWriter outboxWriter,
    TimeProvider timeProvider) : ILedgerService
{
    // Bounds the optimistic-concurrency retry loop below: a losing writer
    // reloads the balance and tries again rather than failing outright, so
    // this is a safety cap against pathological contention, not the
    // expected path.
    private const int MaxVersionRetries = 10;

    public Task<LedgerPostResult> GrantAsync(LedgerMutationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Amount);
        return PostAsync(request, TransactionType.Grant, request.Amount, cancellationToken);
    }

    public Task<LedgerPostResult> SpendAsync(LedgerMutationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Amount);
        return PostAsync(request, TransactionType.Spend, -request.Amount, cancellationToken);
    }

    public Task<LedgerPostResult> AdjustAsync(LedgerMutationRequest request, CancellationToken cancellationToken = default) =>
        PostAsync(request, TransactionType.Adjust, request.Amount, cancellationToken);

    public Task<LedgerPostResult> ConversionDebitAsync(
        LedgerMutationRequest request,
        Func<LedgerPostResult, CancellationToken, Task> onPosted,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Amount);
        return PostAsync(request, TransactionType.ConversionOut, -request.Amount, cancellationToken, onPosted);
    }

    public Task<LedgerPostResult> ConversionCreditAsync(
        LedgerMutationRequest request,
        Func<LedgerPostResult, CancellationToken, Task> onPosted,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Amount);
        return PostAsync(request, TransactionType.ConversionIn, request.Amount, cancellationToken, onPosted);
    }

    public Task<LedgerPostResult> ConversionCompensateAsync(
        LedgerMutationRequest request,
        Func<LedgerPostResult, CancellationToken, Task> onPosted,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Amount);
        return PostAsync(request, TransactionType.Grant, request.Amount, cancellationToken, onPosted);
    }

    private async Task<LedgerPostResult> PostAsync(
        LedgerMutationRequest request,
        TransactionType transactionType,
        decimal signedAmount,
        CancellationToken cancellationToken,
        Func<LedgerPostResult, CancellationToken, Task>? onPosted = null)
    {
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

            // Only this branch - a genuinely new posting - writes to the
            // outbox. The idempotent replay path above returns early and
            // never reaches here, so a retried request never emits a second
            // event for a mutation that only happened once.
            await outboxWriter.WriteAsync(
                new BalanceChangedEvent
                {
                    Id = Guid.CreateVersion7(),
                    OccurredAt = now,
                    LedgerEntryId = entry.Id,
                    UserId = entry.UserId,
                    CurrencyId = entry.CurrencyId,
                    Amount = entry.Amount,
                    Balance = newAmount,
                    TransactionType = entry.TransactionType,
                },
                cancellationToken);

            var result = new LedgerPostResult(entry, newAmount, IsReplay: false);

            if (onPosted is not null)
            {
                await onPosted(result, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
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
