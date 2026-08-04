using EconomyService.Domain;

namespace EconomyService.Services;

public sealed record ConversionCreationRequest(
    Guid ConversionId, Guid UserId, Guid FromCurrencyId, Guid ToCurrencyId, decimal FromAmount);

public sealed record ConversionCreationResult(ConversionRequest Request, bool IsReplay);

public interface IConversionRequestService
{
    Task<ConversionCreationResult> CreateOrGetAsync(
        ConversionCreationRequest request, CancellationToken cancellationToken = default);

    Task<ConversionRate> GetRateAsync(
        Guid fromCurrencyId, Guid toCurrencyId, CancellationToken cancellationToken = default);
}
