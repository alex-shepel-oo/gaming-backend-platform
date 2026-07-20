namespace EconomyService.Services;

public interface IConversionSaga
{
    Task ExecuteAsync(Guid conversionId, CancellationToken cancellationToken = default);
}
