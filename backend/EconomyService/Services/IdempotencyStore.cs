using EconomyService.Domain;
using EconomyService.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EconomyService.Services;

public sealed class IdempotencyStore(EconomyDbContext dbContext) : IIdempotencyStore
{
    public Task<LedgerEntry?> FindExistingAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
        dbContext.LedgerEntries
            .AsNoTracking()
            .SingleOrDefaultAsync(entry => entry.IdempotencyKey == idempotencyKey, cancellationToken);
}
