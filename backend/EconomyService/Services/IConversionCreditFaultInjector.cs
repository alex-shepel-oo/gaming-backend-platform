using EconomyService.Domain;

namespace EconomyService.Services;

// A seam the saga checks immediately before attempting the credit step.
// Production always runs the no-op below; tests substitute a throwing
// implementation to drive the compensating path deterministically, as a
// straightforward step in a sequential call rather than racing a real,
// timing-dependent failure (ADR-0010 addendum).
public interface IConversionCreditFaultInjector
{
    Task BeforeCreditAsync(ConversionRequest request, CancellationToken cancellationToken);
}

public sealed class NoOpConversionCreditFaultInjector : IConversionCreditFaultInjector
{
    public Task BeforeCreditAsync(ConversionRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
}
