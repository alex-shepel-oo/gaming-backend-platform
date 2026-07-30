using EconomyService.Domain;

namespace EconomyService.Services;

public interface IBalanceService
{
    Task<decimal> GetBalanceAsync(Guid userId, Guid currencyId, CancellationToken cancellationToken = default);

    // Every balance row for this user, across every game they hold one in, plus
    // platform balances -- the query is already anchored on UserId, so there is
    // no cross-user leak here, only the full footprint of the caller's own games.
    Task<IReadOnlyList<Balance>> GetBalancesForUserAsync(
        Guid userId, CancellationToken cancellationToken = default);
}
