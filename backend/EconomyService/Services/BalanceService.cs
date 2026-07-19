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
}
