using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Inbox;
using EconomyService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EconomyService.Messaging;

// The first real consumer in the system: everything before this only ever
// published. It binds its own queue to a subset of the topic exchange's
// routing keys - the outbox dispatcher never declared one, since a
// producer-only exchange doesn't need a queue behind it (A.3).
//
// This is self-consumption for demonstration, not a production subscriber:
// EconomyService both emits BalanceChanged/conversion.* events through the
// outbox and, here, consumes them back off the same exchange to show the
// delivery loop (outbox -> broker -> consumer) surviving redelivery. A real
// external subscriber is a later slice's concern.
public sealed class DeduplicatingEventConsumer(
    IRabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    IInboxFaultInjector faultInjector,
    TimeProvider timeProvider,
    ILogger<DeduplicatingEventConsumer> logger,
    // Not a service default a deployer would ever want to change - just a
    // seam so integration tests can each bind their own queue instead of
    // sharing one durable queue across test runs.
    string queueName = "gbp.economy.log-projector")
    : InboxConsumerBase<EconomyDbContext>(
        connection, scopeFactory, faultInjector, timeProvider, logger, options.Value.ExchangeName, queueName, RoutingKeys)
{
    private static readonly string[] RoutingKeys =
    [
        "balance.changed",
        "conversion.debited",
        "conversion.completed",
        "conversion.failed",
    ];

    protected override async Task ApplySideEffectAsync(
        EconomyDbContext dbContext, Guid messageId, string routingKey, ReadOnlyMemory<byte> body, CancellationToken cancellationToken) =>
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO projected_event_counts (event_type, count)
            VALUES ({routingKey}, 1)
            ON CONFLICT (event_type) DO UPDATE SET count = projected_event_counts.count + 1
            """,
            cancellationToken);
}
