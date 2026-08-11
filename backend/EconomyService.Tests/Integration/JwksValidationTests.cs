using System.Net;
using System.Net.Http.Headers;
using AwesomeAssertions;
using BuildingBlocks.Auth;
using EconomyService.Tests.Integration.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace EconomyService.Tests.Integration;

// EconomyService trusts Identity's published RSA key through JwksKeyCache, not a shared HS256
// secret. These tests exercise that resolver end to end (real JwksKeyCache, real background
// refresher, only the HTTP call at the bottom replaced with FakeJwksHandler) rather than
// unit-testing the cache in isolation.
[TestFixture]
public sealed class JwksValidationTests : IAsyncDisposable
{
    private EconomyApiFactory _factory = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUpAsync()
    {
        _factory = new EconomyApiFactory();
        await _factory.InitializeAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync() => await DisposeAsync();

    [SetUp]
    public async Task SetUpAsync() => await _factory.ResetAsync();

    public async ValueTask DisposeAsync()
    {
        _factory.Dispose();
        await _factory.StopContainerAsync();
    }

    [Test]
    public async Task Request_TokenSignedWithIdentitysRealRs256Key_IsAccepted()
    {
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid());
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/balances/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Request_TokenSignedByTreatingThePublicKeyAsAnHmacSecret_IsRejected()
    {
        var token = TestTokenFactory.IssueTokenSignedAsHmacConfusionAttempt(Guid.NewGuid());
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/balances/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Cache_ManyRequestsInARow_FetchesJwksOnlyOnceForTheWholeFactoryLifetime()
    {
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid());
        using var client = _factory.CreateClient();

        // CreateClient() is what actually boots the host (and so triggers Program.cs's own
        // one-time blocking refresh) the first time it's called on this factory; send one
        // request first so that startup fetch is already accounted for, then take the baseline.
        using (var warmupRequest = new HttpRequestMessage(HttpMethod.Get, "/balances/me"))
        {
            warmupRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            await client.SendAsync(warmupRequest, TestContext.CurrentContext.CancellationToken);
        }

        var requestCountAfterWarmup = _factory.JwksHandler.RequestCount;

        for (var i = 0; i < 5; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/balances/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);
        }

        // The whole point of the background-refreshed cache: five more validated requests
        // must not cause five more JWKS fetches, or any at all: the resolver only ever
        // reads the already-warm in-memory snapshot.
        _factory.JwksHandler.RequestCount.Should().Be(requestCountAfterWarmup);
    }

    [Test]
    public async Task Cache_RefreshFailsAfterAGoodFetch_StillValidatesAgainstTheLastKnownGoodSnapshot()
    {
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid());
        var jwksKeyCache = _factory.Services.GetRequiredService<IJwksKeyCache>();

        // The factory's startup refresh already succeeded once (or this call succeeds now if
        // it hadn't yet), either way, there is a good snapshot cached before the endpoint is
        // simulated as unreachable.
        await jwksKeyCache.RefreshAsync(TestContext.CurrentContext.CancellationToken);

        _factory.JwksHandler.ShouldFail = true;
        try
        {
            await jwksKeyCache.RefreshAsync(TestContext.CurrentContext.CancellationToken);
        }
        finally
        {
            _factory.JwksHandler.ShouldFail = false;
        }

        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/balances/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }
}
