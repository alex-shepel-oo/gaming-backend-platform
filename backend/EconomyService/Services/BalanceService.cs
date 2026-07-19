using EconomyService.Domain;
using EconomyService.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EconomyService.Services;

public sealed class BalanceService(EconomyDbContext dbContext) : IBalanceService
{
    public async Task<decimal> GetBalanceAsync(Guid userId, Guid currencyId, CancellationToken cancellationToken = default)
    {
        var balance = await dbContext.Balances
            .AsNoTracking()
            .SingleOrDefaultAsync(b => b.UserId == userId && b.CurrencyId == currencyId, cancellationToken);

        return balance?.Amount ?? 0m;
    }

    public async Task<IReadOnlyList<Balance>> GetBalancesForUserAsync(
        Guid userId, Guid? gameId, CancellationToken cancellationToken = default) =>
        await dbContext.Balances
            .AsNoTracking()
            .Include(b => b.Currency)
            .Where(b => b.UserId == userId && (b.Currency!.GameId == null || b.Currency.GameId == gameId))
            .ToListAsync(cancellationToken);
}
