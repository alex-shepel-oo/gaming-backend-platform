using System.Diagnostics;
using System.Text.Json;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Inbox;
using BuildingBlocks.Telemetry;
using EconomyService.Messaging.Events;
using EconomyService.Persistence;
using EconomyService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EconomyService.Messaging;

// The first real cross-service consumer in the system: binds to identity
// service's own exchange (gbp.identity), not this service's own. A fixed
// integration point between two specific services, not something that
// varies by environment - same reasoning as the seeded Game.Id constants in
// DevelopmentSeeder.
public sealed class UserEmailConfirmedConsumer(
    IRabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    IInboxFaultInjector faultInjector,
    TimeProvider timeProvider,
    ILogger<UserEmailConfirmedConsumer> logger,
    // Not a service default a deployer would ever want to change - just a
    // seam so integration tests can each bind their own queue instead of
    // sharing one durable queue across test runs.
    string queueName = "gbp.economy.welcome-grant")
    : InboxConsumerBase<EconomyDbContext>(
        connection, scopeFactory, faultInjector, timeProvider, logger, ExchangeName, queueName, RoutingKeys)
{
    private const string ExchangeName = "gbp.identity";
    private static readonly string[] RoutingKeys = ["user.email_confirmed"];

    // Assigned explicitly rather than read off the primary constructor
    // parameter directly in ApplySideEffectAsync below: scopeFactory is also
    // forwarded to the base constructor above, and reading the same
    // parameter from an instance method makes the compiler treat it as
    // captured state on this type too (CS9107) - an explicit field says
    // plainly that this is our own copy, independent of whatever the base
    // class does with the one it was handed.
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    protected override async Task ApplySideEffectAsync(
        EconomyDbContext dbContext, Guid messageId, string routingKey, ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<UserEmailConfirmedPayload>(body.Span)!;

        // Already parsed above for the grant itself - tagging the consumer activity (started in
        // InboxConsumerBase.HandleDeliveryAsync, ambient here as Activity.Current) with it is free.
        Activity.Current?.SetTag(OtelConventions.EnduserId, payload.UserId);

        // WelcomeGrantService (-> ILedgerService) is resolved from a fresh
        // scope rather than the scope owning the dbContext parameter above:
        // that dbContext already has a transaction open on it (started by
        // the base class before this method runs), and LedgerService opens
        // its own BeginTransactionAsync internally - nesting explicit
        // transactions on one EF Core context throws. The grant and the
        // inbox's own bookkeeping therefore land in separate transactions,
        // which is fine here because GrantAsync is idempotent on its own
        // idempotency key (welcome:{userId}); a crash between the two just
        // means redelivery replays the grant instead of duplicating it.
        using var scope = _scopeFactory.CreateScope();
        var welcomeGrantService = scope.ServiceProvider.GetRequiredService<IWelcomeGrantService>();

        await welcomeGrantService.GrantAsync(payload.UserId, cancellationToken);
    }
}
