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
public sealed class BalanceEndpointsTests : IAsyncDisposable
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
    public async Task GetMyBalances_ReturnsPlatformAndOwnGameBalances_NotOtherGameOrOtherUsersBalances()
    {
        var gameA = Guid.CreateVersion7();
        var gameB = Guid.CreateVersion7();
        var platformCurrency = await SeedCurrencyAsync("PLATFORM_CREDITS", CurrencyScope.Platform, null);
        var gameACurrency = await SeedCurrencyAsync("GAME_A_GOLD", CurrencyScope.Game, gameA);
        var gameBCurrency = await SeedCurrencyAsync("GAME_B_GOLD", CurrencyScope.Game, gameB);

        var userId = Guid.NewGuid();
        await SeedBalanceAsync(userId, platformCurrency.Id, 100m);
        await SeedBalanceAsync(userId, gameACurrency.Id, 50m);

        // A balance left over from a session in a different game must never
        // surface, no matter which game the caller is currently scoped to.
        await SeedBalanceAsync(userId, gameBCurrency.Id, 999m);

        var otherUserId = Guid.NewGuid();
        await SeedBalanceAsync(otherUserId, platformCurrency.Id, 5000m);

        var token = TestTokenFactory.IssueAccessToken(userId, gameA);
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/balances/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BalanceDto[]>(
            JsonOptions, TestContext.CurrentContext.CancellationToken);

        body!.Should().HaveCount(2);
        body.Select(b => b.CurrencyId).Should().Contain([platformCurrency.Id, gameACurrency.Id]);
        body.Select(b => b.CurrencyId).Should().NotContain(gameBCurrency.Id);
        body.Single(b => b.CurrencyId == platformCurrency.Id).Amount.Should().Be(100m);
        body.Single(b => b.CurrencyId == gameACurrency.Id).Amount.Should().Be(50m);
    }

    [Test]
    public async Task GetMyBalances_NoAuthorizationHeader_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/balances/me", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

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

    private async Task SeedBalanceAsync(Guid userId, Guid currencyId, decimal amount)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EconomyDbContext>();

        dbContext.Balances.Add(new Balance
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            CurrencyId = currencyId,
            Amount = amount,
            Version = 0,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await dbContext.SaveChangesAsync(TestContext.CurrentContext.CancellationToken);
    }
}
