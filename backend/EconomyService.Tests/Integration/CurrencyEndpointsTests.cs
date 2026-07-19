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
    public async Task GetCurrencies_ReturnsPlatformCurrenciesAndOwnGameCurrency_NotOtherGamesCurrency()
    {
        var gameA = Guid.CreateVersion7();
        var gameB = Guid.CreateVersion7();
        var platform = await SeedCurrencyAsync("PLATFORM_CREDITS", CurrencyScope.Platform, null);
        var gameACurrency = await SeedCurrencyAsync("GAME_A_GOLD", CurrencyScope.Game, gameA);
        var gameBCurrency = await SeedCurrencyAsync("GAME_B_GOLD", CurrencyScope.Game, gameB);

        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), gameA);
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/currencies");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CurrencyDto[]>(
            JsonOptions, TestContext.CurrentContext.CancellationToken);

        var ids = body!.Select(c => c.Id).ToArray();
        ids.Should().Contain([platform.Id, gameACurrency.Id]);
        ids.Should().NotContain(gameBCurrency.Id);
    }

    [Test]
    public async Task GetCurrencies_NoAuthorizationHeader_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/currencies", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<Currency> SeedCurrencyAsync(string code, CurrencyScope scope, Guid? gameId)
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
            CreatedAt = DateTimeOffset.UtcNow,
        };

        dbContext.Currencies.Add(currency);
        await dbContext.SaveChangesAsync(TestContext.CurrentContext.CancellationToken);

        return currency;
    }
}
