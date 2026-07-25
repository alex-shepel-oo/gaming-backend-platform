using AwesomeAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Hubs;
using NotificationService.Tests.Integration.Fixtures;
using Xunit;

namespace NotificationService.Tests.Integration;

[Collection(nameof(NotificationApiCollectionDefinition))]
public sealed class NotificationHubTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Connect_ValidToken_Succeeds()
    {
        await using var connection = BuildConnection(TestTokenFactory.IssueAccessToken(Guid.NewGuid()));

        var connect = async () => await connection.StartAsync(TestContext.Current.CancellationToken);

        await connect.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Connect_MissingToken_Throws()
    {
        await using var connection = BuildConnection(accessToken: null);

        var connect = async () => await connection.StartAsync(TestContext.Current.CancellationToken);

        await connect.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Connect_InvalidToken_Throws()
    {
        await using var connection = BuildConnection("not-a-valid-jwt");

        var connect = async () => await connection.StartAsync(TestContext.Current.CancellationToken);

        await connect.Should().ThrowAsync<Exception>();
    }

    // Guards SubClaimUserIdProvider: without it, Clients.User(id) matches no
    // connection and this test hangs until the timeout instead of failing fast.
    [Fact]
    public async Task ClientsUser_DeliversOnlyToMatchingConnection()
    {
        var targetUserId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        await using var targetConnection = BuildConnection(TestTokenFactory.IssueAccessToken(targetUserId));
        await using var otherConnection = BuildConnection(TestTokenFactory.IssueAccessToken(otherUserId));

        var targetReceived = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var otherReceived = false;

        targetConnection.On<string>("balanceChanged", payload => targetReceived.TrySetResult(payload));
        otherConnection.On<string>("balanceChanged", _ => otherReceived = true);

        await targetConnection.StartAsync(TestContext.Current.CancellationToken);
        await otherConnection.StartAsync(TestContext.Current.CancellationToken);

        var hubContext = factory.Services.GetRequiredService<IHubContext<NotificationHub>>();
        await hubContext.Clients.User(targetUserId.ToString())
            .SendAsync("balanceChanged", "test-payload", TestContext.Current.CancellationToken);

        using var timeoutCts = new CancellationTokenSource(DeliveryTimeout);
        using var registration = timeoutCts.Token.Register(() => targetReceived.TrySetCanceled(timeoutCts.Token));

        var received = await targetReceived.Task;

        received.Should().Be("test-payload");
        otherReceived.Should().BeFalse();
    }

    private HubConnection BuildConnection(string? accessToken) =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/notifications"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();

                // TestServer has no real socket to upgrade, so WebSockets (the
                // default negotiated transport) never connects; LongPolling works
                // over the same in-memory HttpMessageHandler.
                options.Transports = HttpTransportType.LongPolling;

                if (accessToken is not null)
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                }
            })
            .Build();
}
