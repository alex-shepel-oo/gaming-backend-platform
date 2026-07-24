using EconomyService.Domain;

namespace EconomyService.Services;

public interface IBalanceService
{
    Task<decimal> GetBalanceAsync(Guid userId, Guid currencyId, CancellationToken cancellationToken = default);

    // Scoped by gameId at the query level (platform currencies plus, when gameId
    // is supplied, that game's currencies) so a caller's own leftover balances
    // from a different game session can never surface in the result.
    Task<IReadOnlyList<Balance>> GetBalancesForUserAsync(
        Guid userId, Guid? gameId, CancellationToken cancellationToken = default);
}
