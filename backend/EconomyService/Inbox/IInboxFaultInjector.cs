namespace EconomyService.Inbox;

// A seam the consumer checks after applying the side effect but before
// committing the transaction. Production always runs the no-op below; tests
// substitute a throwing implementation to simulate a crash between the side
// effect and the commit, so redelivery of the same message is a deterministic
// step to test rather than a real timing-dependent failure.
public interface IInboxFaultInjector
{
    Task BeforeCommitAsync(Guid messageId, CancellationToken cancellationToken);
}

public sealed class NoOpInboxFaultInjector : IInboxFaultInjector
{
    public Task BeforeCommitAsync(Guid messageId, CancellationToken cancellationToken) => Task.CompletedTask;
}
