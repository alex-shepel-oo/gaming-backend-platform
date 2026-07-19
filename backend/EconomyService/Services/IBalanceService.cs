namespace EconomyService.Services;

public interface IBalanceService
{
    Task<decimal> GetBalanceAsync(Guid userId, Guid currencyId, CancellationToken cancellationToken = default);
}
