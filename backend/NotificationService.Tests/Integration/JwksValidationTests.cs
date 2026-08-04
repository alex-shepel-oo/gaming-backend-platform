using AwesomeAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Auth;
using NotificationService.Tests.Integration.Fixtures;
using Xunit;

namespace NotificationService.Tests.Integration;

// Session 2 of Group A9: NotificationService no longer trusts a shared HS256 secret, it
// resolves Identity's published RSA key through JwksKeyCache instead. These tests exercise
// that resolver end to end (real JwksKeyCache, real background refresher, only the HTTP call
// at the bottom replaced with FakeJwksHandler) through the same query-string-token hub
// connection path NotificationHubTests already covers, rather than unit-testing the cache in
// isolation.
[Collection(nameof(NotificationApiCollectionDefinition))]
public sealed class JwksValidationTests(NotificationApiFactory factory) : IClassFixture<NotificationApiFactory>
{
    [Fact]
    public async Task Connect_TokenSignedWithIdentitysRealRs256Key_Succeeds()
    {
        await using var connection = BuildConnection(TestTokenFactory.IssueAccessToken(Guid.NewGuid()));

        var connect = async () => await connection.StartAsync(TestContext.Current.CancellationToken);

        await connect.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Connect_TokenSignedByTreatingThePublicKeyAsAnHmacSecret_Throws()
    {
        await using var connection = BuildConnection(TestTokenFactory.IssueTokenSignedAsHmacConfusionAttempt(Guid.NewGuid()));

        var connect = async () => await connection.StartAsync(TestContext.Current.CancellationToken);

        await connect.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Cache_SeveralConnectionsInARow_FetchesJwksOnlyOnceForTheWholeFactoryLifetime()
    {
        // The very first connection is what actually boots the host (and so triggers
        // Program.cs's own one-time blocking refresh) -- let that happen and settle before
        // taking the baseline.
        await using (var warmupConnection = BuildConnection(TestTokenFactory.IssueAccessToken(Guid.NewGuid())))
        {
            await warmupConnection.StartAsync(TestContext.Current.CancellationToken);
        }

        var requestCountAfterWarmup = factory.JwksHandler.RequestCount;

        for (var i = 0; i < 3; i++)
        {
            await using var connection = BuildConnection(TestTokenFactory.IssueAccessToken(Guid.NewGuid()));
            await connection.StartAsync(TestContext.Current.CancellationToken);
        }

        // The whole point of the background-refreshed cache: three more validated
        // connections must not cause three more JWKS fetches, or any at all -- the resolver
        // only ever reads the already-warm in-memory snapshot.
        factory.JwksHandler.RequestCount.Should().Be(requestCountAfterWarmup);
    }

    [Fact]
    public async Task Cache_RefreshFailsAfterAGoodFetch_StillValidatesAgainstTheLastKnownGoodSnapshot()
    {
        var jwksKeyCache = factory.Services.GetRequiredService<IJwksKeyCache>();

        // The factory's startup refresh already succeeded once (or this call succeeds now if
        // it hadn't yet) -- either way, there is a good snapshot cached before the endpoint is
        // simulated as unreachable.
        await jwksKeyCache.RefreshAsync(TestContext.Current.CancellationToken);

        factory.JwksHandler.ShouldFail = true;
        try
        {
            await jwksKeyCache.RefreshAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            factory.JwksHandler.ShouldFail = false;
        }

        await using var connection = BuildConnection(TestTokenFactory.IssueAccessToken(Guid.NewGuid()));

        var connect = async () => await connection.StartAsync(TestContext.Current.CancellationToken);

        await connect.Should().NotThrowAsync();
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
}
