namespace EconomyService.Services;

public interface IWelcomeGrantService
{
    Task GrantAsync(Guid userId, CancellationToken cancellationToken = default);
}
