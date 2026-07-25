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

    private async Task PublishRawAsync(string payload, CancellationToken cancellationToken)
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
        await eventBus.PublishAsync(new EventEnvelope("balance.changed", 1, payload), cancellationToken);
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
