using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using NotificationService.Tests.Integration.Fixtures;
using Xunit;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace NotificationService.Tests.Integration;

[Collection(nameof(NotificationApiCollectionDefinition))]
public sealed class BalanceChangedConsumerTests(NotificationApiFactory factory, RabbitMqFixture rabbitMq)
    : IClassFixture<NotificationApiFactory>
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task BalanceChanged_PublishedForConnectedUser_PushesOnlyToThatUser()
    {
        var targetUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        await using var targetConnection = BuildConnection(TestTokenFactory.IssueAccessToken(targetUserId));
        await using var otherConnection = BuildConnection(TestTokenFactory.IssueAccessToken(otherUserId));

        var targetReceived = new TaskCompletionSource<BalanceChangedPush>(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherReceived = false;

        targetConnection.On<BalanceChangedPush>("balanceChanged", push =>
        {
            targetReceived.TrySetResult(push);
            return Task.CompletedTask;
        });
        otherConnection.On<BalanceChangedPush>("balanceChanged", _ =>
        {
            otherReceived = true;
            return Task.CompletedTask;
        });

        await targetConnection.StartAsync(TestContext.Current.CancellationToken);
        await otherConnection.StartAsync(TestContext.Current.CancellationToken);

        var currencyId = Guid.NewGuid();
        const decimal amount = 25.50m;
        const decimal balance = 125.75m;

        await PublishBalanceChangedAsync(targetUserId, currencyId, amount, balance, TestContext.Current.CancellationToken);

        using var timeoutCts = new CancellationTokenSource(DeliveryTimeout);
        using var registration = timeoutCts.Token.Register(() => targetReceived.TrySetCanceled(timeoutCts.Token));

        var received = await targetReceived.Task;

        received.CurrencyId.Should().Be(currencyId);
        received.Amount.Should().Be(amount);
        received.Balance.Should().Be(balance);
        otherReceived.Should().BeFalse();
    }

    // BalanceChangedConsumer is the hand-rolled consumer shape (no InboxConsumerBase, no DbContext -
    // see the type-level comment on it), so it needs its own proof that the shared
    // MessagingTracePropagation helper still parents its Consumer activity to the delivery's
    // traceparent header, and that it tags enduser.id from the payload's UserId - the same
    // information already needed for the SignalR routing below, not a new field added for this.
    [Fact]
    public async Task BalanceChanged_MessageWithTraceParentHeader_ConsumerActivityIsParentedToSameTraceAndTaggedWithUserId()
    {
        var targetUserId = Guid.NewGuid();
        var currencyId = Guid.NewGuid();

        // Marked Recorded explicitly - an unmarked legacy Activity defaults to "00" (unsampled) trace
        // flags, which would make the default ParentBasedSampler correctly, but misleadingly for this
        // test, decline to sample the consumer activity below.
        using var seedActivity = new Activity("seed-publish").Start();
        seedActivity.ActivityTraceFlags = ActivityTraceFlags.Recorded;
        var traceParent = seedActivity.Id!;

        var capturedActivities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "BuildingBlocks.Messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = capturedActivities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await using var targetConnection = BuildConnection(TestTokenFactory.IssueAccessToken(targetUserId));
        var received = new TaskCompletionSource<BalanceChangedPush>(TaskCreationOptions.RunContinuationsAsynchronously);
        targetConnection.On<BalanceChangedPush>("balanceChanged", push =>
        {
            received.TrySetResult(push);
            return Task.CompletedTask;
        });
        await targetConnection.StartAsync(TestContext.Current.CancellationToken);

        await PublishBalanceChangedWithTraceParentAsync(
            targetUserId, currencyId, 10m, 50m, traceParent, TestContext.Current.CancellationToken);

        using var timeoutCts = new CancellationTokenSource(DeliveryTimeout);
        using var registration = timeoutCts.Token.Register(() => received.TrySetCanceled(timeoutCts.Token));
        await received.Task;

        // ActivityStopped fires once HandleDeliveryAsync's using scope disposes the activity, which
        // happens right after (not necessarily strictly before) the SignalR send above completes -
        // poll briefly rather than asserting immediately against a possible race.
        Activity? consumerActivity = null;
        await WaitUntilAsync(
            () =>
            {
                consumerActivity = capturedActivities.FirstOrDefault(a => a.TraceId == seedActivity.TraceId);
                return Task.FromResult(consumerActivity is not null);
            },
            TimeSpan.FromSeconds(10));

        consumerActivity.Should().NotBeNull();
        consumerActivity!.Kind.Should().Be(ActivityKind.Consumer);

        // TagObjects, not Tags: SetTag(string, object?) - what BalanceChangedConsumer and the
        // enduser.id middleware both call - populates TagObjects, not the legacy string-only Tags
        // property (that one is only ever populated by the older AddTag(string, string?) method).
        consumerActivity.TagObjects.Should().Contain(t => t.Key == "enduser.id" && Equals(t.Value, targetUserId));
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token);
        }

        Assert.Fail($"Condition was not met within {timeout}.");
    }

    // Guards the consumer's catch-and-ack path: without it, an undecodable
    // delivery would either crash the BackgroundService (and, by default,
    // the whole host) or sit unacked and block the queue forever.
    [Fact]
    public async Task BalanceChanged_UndecodablePayload_IsAckedAndServiceStaysHealthy()
    {
        await PublishRawAsync("not-json", TestContext.Current.CancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        using var client = factory.CreateClient();
        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task PublishBalanceChangedAsync(
        Guid userId, Guid currencyId, decimal amount, decimal balance, CancellationToken cancellationToken)
    {
        // Mirrors the raw wire shape RabbitMqEventBus actually produces for
        // BalanceChangedEvent (LedgerEntryId/TransactionType/Id/OccurredAt
        // included, Version as the type discriminator) so the consumer is
        // exercised against the real payload, not a simplified stand-in.
        var payload = JsonSerializer.Serialize(new
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            LedgerEntryId = Guid.NewGuid(),
            UserId = userId,
            CurrencyId = currencyId,
            Amount = amount,
            Balance = balance,
            TransactionType = "Credit",
            Type = "balance.changed",
            Version = 1,
        });

        await PublishRawAsync(payload, cancellationToken);
    }

    // Simulates what the outbox dispatcher's PublishOneAsync actually sends: the same payload shape
    // as PublishBalanceChangedAsync above, plus the W3C traceparent header a real Producer activity
    // would have injected.
    private async Task PublishBalanceChangedWithTraceParentAsync(
        Guid userId, Guid currencyId, decimal amount, decimal balance, string traceParent, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            Id = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            LedgerEntryId = Guid.NewGuid(),
            UserId = userId,
            CurrencyId = currencyId,
            Amount = amount,
            Balance = balance,
            TransactionType = "Credit",
            Type = "balance.changed",
            Version = 1,
        });

        await PublishRawAsync(payload, cancellationToken, traceParent);
    }

    private async Task PublishRawAsync(string payload, CancellationToken cancellationToken, string? traceParent = null)
    {
        var options = MsOptions.Create(new RabbitMqOptions
        {
            Host = rabbitMq.Container.Hostname,
            Port = rabbitMq.Container.GetMappedPublicPort(5672),
            Username = "guest",
            Password = "guest",
        });

        await using var connection = new RabbitMqConnection(options);
        var eventBus = new RabbitMqEventBus(connection, options);
        var headers = traceParent is null ? null : new Dictionary<string, string> { ["traceparent"] = traceParent };
        await eventBus.PublishAsync(new EventEnvelope("balance.changed", 1, payload), headers, cancellationToken);
    }

    private HubConnection BuildConnection(string accessToken) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/notifications"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
            })
            .Build();

    private sealed record BalanceChangedPush(Guid CurrencyId, decimal Amount, decimal Balance);
}
