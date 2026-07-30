using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using EconomyService.Auth;
using EconomyService.Contracts.Requests;
using EconomyService.Contracts.Responses;
using EconomyService.Domain;
using EconomyService.Domain.Enums;
using EconomyService.Persistence;
using EconomyService.Tests.Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
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
    public async Task GetMyBalances_ReturnsBalancesAcrossEveryGame_RegardlessOfCurrentTokenScope_ButNotOtherUsersBalances()
    {
        var gameA = Guid.CreateVersion7();
        var gameB = Guid.CreateVersion7();
        var platformCurrency = await SeedCurrencyAsync("PLATFORM_CREDITS", CurrencyScope.Platform, null);
        var gameACurrency = await SeedCurrencyAsync("GAME_A_GOLD", CurrencyScope.Game, gameA);
        var gameBCurrency = await SeedCurrencyAsync("GAME_B_GOLD", CurrencyScope.Game, gameB);

        var userId = Guid.NewGuid();
        await SeedBalanceAsync(userId, platformCurrency.Id, 100m);
        await SeedBalanceAsync(userId, gameACurrency.Id, 50m);

        // A balance held in a game the caller isn't currently scoped into must
        // still come back -- the player's whole cross-game footprint, not just
        // whichever single game their token happens to be scoped to right now.
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

        body!.Should().HaveCount(3);
        body.Select(b => b.CurrencyId).Should().Contain([platformCurrency.Id, gameACurrency.Id, gameBCurrency.Id]);
        body.Single(b => b.CurrencyId == platformCurrency.Id).Amount.Should().Be(100m);
        body.Single(b => b.CurrencyId == gameACurrency.Id).Amount.Should().Be(50m);
        body.Single(b => b.CurrencyId == gameBCurrency.Id).Amount.Should().Be(999m);
    }

    [Test]
    public async Task GetMyBalances_CallerHasNoGameScopeInToken_StillReturnsAllOwnGameBalances()
    {
        var gameA = Guid.CreateVersion7();
        var platformCurrency = await SeedCurrencyAsync("PLATFORM_CREDITS", CurrencyScope.Platform, null);
        var gameACurrency = await SeedCurrencyAsync("GAME_A_GOLD", CurrencyScope.Game, gameA);

        var userId = Guid.NewGuid();
        await SeedBalanceAsync(userId, platformCurrency.Id, 25m);
        await SeedBalanceAsync(userId, gameACurrency.Id, 10m);

        // No gameId at all on the token (account-scoped session) -- balances
        // are keyed off the caller's own userId, not the token's game scope.
        var token = TestTokenFactory.IssueAccessToken(userId);
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/balances/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BalanceDto[]>(
            JsonOptions, TestContext.CurrentContext.CancellationToken);

        body!.Should().HaveCount(2);
        body.Select(b => b.CurrencyId).Should().Contain([platformCurrency.Id, gameACurrency.Id]);
    }

    [Test]
    public async Task GetMyBalances_NoAuthorizationHeader_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/balances/me", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Adjust_CallerIsNotAdmin_Returns403()
    {
        var currency = await SeedCurrencyAsync("PLATFORM_CREDITS", CurrencyScope.Platform, null);
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), role: "Moderator");

        var response = await AdjustAsync(
            Guid.NewGuid(), new AdjustRequest(currency.Id, 25m, "correction"), token, "adjust-key-1");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Adjust_AdminNoReason_Returns400()
    {
        var currency = await SeedCurrencyAsync("PLATFORM_CREDITS", CurrencyScope.Platform, null);
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), perms: [Permissions.PlatformBalanceAdjust]);

        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/balances/{Guid.NewGuid()}/adjust")
        {
            Content = JsonContent.Create(new { currency.Id, Amount = 25m }, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Idempotency-Key", "adjust-key-2");

        var response = await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Adjust_AdminWithReasonAndIdempotencyKey_Returns201AndPostsLedgerEntry()
    {
        var currency = await SeedCurrencyAsync("PLATFORM_CREDITS", CurrencyScope.Platform, null);
        var userId = Guid.NewGuid();
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), perms: [Permissions.PlatformBalanceAdjust]);

        var response = await AdjustAsync(
            userId, new AdjustRequest(currency.Id, 15m, "manual correction"), token, "adjust-key-3");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<TransactionDto>(JsonOptions, TestContext.CurrentContext.CancellationToken);
        body!.TransactionType.Should().Be(TransactionType.Adjust);
        body.Balance.Should().Be(15m);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EconomyDbContext>();
        var entries = await dbContext.LedgerEntries
            .Where(e => e.UserId == userId && e.CurrencyId == currency.Id)
            .ToListAsync(TestContext.CurrentContext.CancellationToken);
        entries.Should().HaveCount(1);
    }

    [Test]
    public async Task Adjust_GameAdminOwnGameCurrency_Returns201()
    {
        var gameId = Guid.CreateVersion7();
        var currency = await SeedCurrencyAsync("GAME_GOLD", CurrencyScope.Game, gameId);
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), gameId, perms: [Permissions.GameBalanceAdjust]);

        var response = await AdjustAsync(
            Guid.NewGuid(), new AdjustRequest(currency.Id, 15m, "game admin correction"), token, "adjust-key-gameadmin-own");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Test]
    public async Task Adjust_GameAdminOtherGameCurrency_Returns403()
    {
        var ownGameId = Guid.CreateVersion7();
        var otherGameId = Guid.CreateVersion7();
        var currency = await SeedCurrencyAsync("OTHER_GAME_GOLD", CurrencyScope.Game, otherGameId);
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), ownGameId, perms: [Permissions.GameBalanceAdjust]);

        var response = await AdjustAsync(
            Guid.NewGuid(), new AdjustRequest(currency.Id, 15m, "wrong game"), token, "adjust-key-gameadmin-other-game");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Adjust_GameAdminPlatformCurrency_Returns403()
    {
        var gameId = Guid.CreateVersion7();
        var currency = await SeedCurrencyAsync("PLATFORM_CREDITS", CurrencyScope.Platform, null);
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), gameId, perms: [Permissions.GameBalanceAdjust]);

        var response = await AdjustAsync(
            Guid.NewGuid(), new AdjustRequest(currency.Id, 15m, "platform currency"), token, "adjust-key-gameadmin-platform-currency");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Adjust_PlatformAdminOtherGameCurrency_Returns201()
    {
        var gameId = Guid.CreateVersion7();
        var currency = await SeedCurrencyAsync("GAME_GOLD", CurrencyScope.Game, gameId);
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), perms: [Permissions.PlatformBalanceAdjust]);

        var response = await AdjustAsync(
            Guid.NewGuid(), new AdjustRequest(currency.Id, 15m, "platform admin correction"), token, "adjust-key-platformadmin-other-game");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<HttpResponseMessage> AdjustAsync(Guid userId, AdjustRequest requestBody, string token, string? idempotencyKey)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/balances/{userId}/adjust")
        {
            Content = JsonContent.Create(requestBody, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);
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
            Decimals = 2,
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
