using EconomyService.Domain;

namespace EconomyService.Services;

public interface IIdempotencyStore
{
    Task<LedgerEntry?> FindExistingAsync(string idempotencyKey, CancellationToken cancellationToken = default);
}
