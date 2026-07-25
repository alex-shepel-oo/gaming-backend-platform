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
public sealed class TenantIsolationTests : IAsyncDisposable
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
    public async Task GetMyBalances_GameIdQueryDoesNotMatchTokenGame_Returns403()
    {
        var gameA = Guid.CreateVersion7();
        var gameB = Guid.CreateVersion7();

        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), gameA);
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/balances/me?gameId={gameB}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task GetMyBalances_GameIdQueryMatchesTokenGame_Returns200()
    {
        var gameA = Guid.CreateVersion7();
        var userId = Guid.NewGuid();
        var platformCurrency = await SeedCurrencyAsync("PLATFORM_CREDITS", CurrencyScope.Platform, null);
        await SeedBalanceAsync(userId, platformCurrency.Id, 10m);

        var token = TestTokenFactory.IssueAccessToken(userId, gameA);
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/balances/me?gameId={gameA}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BalanceDto[]>(
            JsonOptions, TestContext.CurrentContext.CancellationToken);
        body!.Should().ContainSingle(b => b.CurrencyId == platformCurrency.Id);
    }

    [Test]
    public async Task GetMyBalances_NoAuthorizationHeader_GameIdQueryPresent_Returns401NotForbidden()
    {
        var gameB = Guid.CreateVersion7();
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(
            new Uri($"/balances/me?gameId={gameB}", UriKind.Relative), TestContext.CurrentContext.CancellationToken);

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
