using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
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
public sealed class TransactionEndpointsTests : IAsyncDisposable
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
    public async Task Grant_AdminWithIdempotencyKey_Returns201AndPostsLedgerEntry()
    {
        var currency = await SeedCurrencyAsync();
        var userId = Guid.NewGuid();
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), role: "Admin");

        var response = await PostAsync(
            "/transactions/grant",
            new GrantRequest(userId, currency.Id, 100m, "welcome bonus"),
            token,
            "grant-key-1");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<TransactionDto>(JsonOptions, TestContext.CurrentContext.CancellationToken);
        body!.TransactionType.Should().Be(TransactionType.Grant);
        body.Balance.Should().Be(100m);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EconomyDbContext>();
        var entries = await dbContext.LedgerEntries
            .Where(e => e.UserId == userId && e.CurrencyId == currency.Id)
            .ToListAsync(TestContext.CurrentContext.CancellationToken);
        entries.Should().HaveCount(1);
    }

    [Test]
    public async Task Grant_CallerIsPlayer_Returns403()
    {
        var currency = await SeedCurrencyAsync();
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), role: "Player");

        var response = await PostAsync(
            "/transactions/grant",
            new GrantRequest(Guid.NewGuid(), currency.Id, 100m, "welcome bonus"),
            token,
            "grant-key-2");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task Spend_WithinBalance_Returns201AndDebitsBalance()
    {
        var currency = await SeedCurrencyAsync();
        var userId = Guid.NewGuid();
        var adminToken = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), role: "Admin");
        await PostAsync("/transactions/grant", new GrantRequest(userId, currency.Id, 100m, "seed"), adminToken, "seed-grant-1");

        var playerToken = TestTokenFactory.IssueAccessToken(userId, role: "Player");
        var response = await PostAsync(
            "/transactions/spend", new SpendRequest(currency.Id, 30m, null), playerToken, "spend-key-1");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<TransactionDto>(JsonOptions, TestContext.CurrentContext.CancellationToken);
        body!.Balance.Should().Be(70m);
    }

    [Test]
    public async Task Spend_MoreThanBalance_Returns402AndBalanceUnchanged()
    {
        var currency = await SeedCurrencyAsync();
        var userId = Guid.NewGuid();
        var adminToken = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), role: "Admin");
        await PostAsync("/transactions/grant", new GrantRequest(userId, currency.Id, 50m, "seed"), adminToken, "seed-grant-2");

        var playerToken = TestTokenFactory.IssueAccessToken(userId, role: "Player");
        var response = await PostAsync(
            "/transactions/spend", new SpendRequest(currency.Id, 200m, null), playerToken, "spend-key-2");

        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EconomyDbContext>();
        var balance = await dbContext.Balances
            .SingleAsync(b => b.UserId == userId && b.CurrencyId == currency.Id, TestContext.CurrentContext.CancellationToken);
        balance.Amount.Should().Be(50m);
        balance.Amount.Should().BeGreaterThanOrEqualTo(0m);
    }

    [Test]
    public async Task Grant_RepeatedIdempotencyKey_DoesNotDoublePost_ReturnsSameResult()
    {
        var currency = await SeedCurrencyAsync();
        var userId = Guid.NewGuid();
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), role: "Moderator");
        var request = new GrantRequest(userId, currency.Id, 25m, "sign-up grant");

        var firstResponse = await PostAsync("/transactions/grant", request, token, "replay-key-1");
        var secondResponse = await PostAsync("/transactions/grant", request, token, "replay-key-1");

        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var firstBody = await firstResponse.Content.ReadFromJsonAsync<TransactionDto>(JsonOptions, TestContext.CurrentContext.CancellationToken);
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<TransactionDto>(JsonOptions, TestContext.CurrentContext.CancellationToken);
        secondBody!.Id.Should().Be(firstBody!.Id);
        secondBody.Balance.Should().Be(firstBody.Balance);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EconomyDbContext>();
        var entryCount = await dbContext.LedgerEntries
            .CountAsync(e => e.IdempotencyKey == "replay-key-1", TestContext.CurrentContext.CancellationToken);
        entryCount.Should().Be(1);
    }

    [Test]
    public async Task Grant_NoIdempotencyKeyHeader_Returns400()
    {
        var currency = await SeedCurrencyAsync();
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), role: "Admin");

        var response = await PostAsync(
            "/transactions/grant", new GrantRequest(Guid.NewGuid(), currency.Id, 10m, "no key"), token, idempotencyKey: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Spend_NoIdempotencyKeyHeader_Returns400()
    {
        var currency = await SeedCurrencyAsync();
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), role: "Player");

        var response = await PostAsync(
            "/transactions/spend", new SpendRequest(currency.Id, 10m, null), token, idempotencyKey: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Grant_NoAuthorizationHeader_Returns401()
    {
        var currency = await SeedCurrencyAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/transactions/grant",
            new GrantRequest(Guid.NewGuid(), currency.Id, 10m, "no auth"),
            JsonOptions,
            TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Spend_NoAuthorizationHeader_Returns401()
    {
        var currency = await SeedCurrencyAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/transactions/spend",
            new SpendRequest(currency.Id, 10m, null),
            JsonOptions,
            TestContext.CurrentContext.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpResponseMessage> PostAsync<TRequest>(
        string path, TRequest requestBody, string token, string? idempotencyKey)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
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

    private async Task<Currency> SeedCurrencyAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EconomyDbContext>();

        var currency = new Currency
        {
            Id = Guid.CreateVersion7(),
            Code = $"TEST_{Guid.NewGuid():N}",
            DisplayName = "Test Credits",
            Scope = CurrencyScope.Platform,
            GameId = null,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        dbContext.Currencies.Add(currency);
        await dbContext.SaveChangesAsync(TestContext.CurrentContext.CancellationToken);

        return currency;
    }
}
