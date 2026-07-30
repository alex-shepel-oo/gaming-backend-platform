using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using EconomyService.Contracts.Responses;
using EconomyService.Domain;
using EconomyService.Domain.Enums;
using EconomyService.Persistence;
using EconomyService.Tests.Integration.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace EconomyService.Tests.Integration;

[TestFixture]
public sealed class CurrencyEndpointsTests : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
    public async Task GetCurrencies_ReturnsEveryGamesCurrency_RegardlessOfCallerScope()
    {
        var gameA = Guid.CreateVersion7();
        var gameB = Guid.CreateVersion7();
        var platform = await SeedCurrencyAsync("PLATFORM_CREDITS", CurrencyScope.Platform, null);
        var gameACurrency = await SeedCurrencyAsync("GAME_A_GOLD", CurrencyScope.Game, gameA);
        var gameBCurrency = await SeedCurrencyAsync("GAME_B_GOLD", CurrencyScope.Game, gameB);

        // Caller's token is scoped to gameA only, yet gameB's currency -- one the
        // caller has never entered -- must still appear: this is the read-only
        // catalog, not a per-user balance, so there is nothing to scope it by.
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), gameA);
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/currencies");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CurrencyDto[]>(
            JsonOptions, TestContext.CurrentContext.CancellationToken);

        var ids = body!.Select(c => c.Id).ToArray();
        ids.Should().Contain([platform.Id, gameACurrency.Id, gameBCurrency.Id]);
        body!.Should().OnlyContain(c => c.Decimals == 2);
    }

    [Test]
    public async Task GetCurrencies_CurrencyWithNonDefaultDecimals_ReturnsConfiguredValue()
    {
        var currency = await SeedCurrencyAsync("HIGH_PRECISION", CurrencyScope.Platform, null, decimals: 4);

        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), Guid.NewGuid());
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/currencies");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CurrencyDto[]>(
            JsonOptions, TestContext.CurrentContext.CancellationToken);

        body!.Single(c => c.Id == currency.Id).Decimals.Should().Be(4);
    }

    [Test]
    public async Task GetCurrencies_NoAuthorizationHeader_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/currencies", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<Currency> SeedCurrencyAsync(string code, CurrencyScope scope, Guid? gameId, short decimals = 2)
    {
        await using var scope1 = _factory.Services.CreateAsyncScope();
        var dbContext = scope1.ServiceProvider.GetRequiredService<EconomyDbContext>();

        var currency = new Currency
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            DisplayName = code,
            Scope = scope,
            GameId = gameId,
            Decimals = decimals,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        dbContext.Currencies.Add(currency);
        await dbContext.SaveChangesAsync(TestContext.CurrentContext.CancellationToken);

        return currency;
    }
}
