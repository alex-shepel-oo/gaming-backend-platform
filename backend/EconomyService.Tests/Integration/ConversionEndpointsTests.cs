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
public sealed class ConversionEndpointsTests : IAsyncDisposable
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
    public async Task Create_ValidRequest_Returns202StartedAndSagaRunsToCompleted()
    {
        var (fromCurrencyId, toCurrencyId, _) = await SeedCurrencyPairAsync();
        var userId = Guid.NewGuid();
        var token = TestTokenFactory.IssueAccessToken(userId);
        await SeedPlatformBalanceAsync(userId, fromCurrencyId, 100m, "conversion-endpoint-seed-1");

        var response = await PostAsync(
            new ConvertRequest(fromCurrencyId, toCurrencyId, 10m), token, Guid.NewGuid().ToString());

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<ConversionDto>(JsonOptions, TestContext.CurrentContext.CancellationToken);
        body!.Status.Should().Be(ConversionStatus.Started);

        var finalStatus = await PollUntilTerminalAsync(body.ConversionId, token);
        finalStatus.Should().Be(ConversionStatus.Completed);
    }

    [Test]
    public async Task Create_InsufficientFunds_Returns402AndDoesNotCreateRow()
    {
        var (fromCurrencyId, toCurrencyId, _) = await SeedCurrencyPairAsync();
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid());
        var conversionId = Guid.NewGuid();

        var response = await PostAsync(
            new ConvertRequest(fromCurrencyId, toCurrencyId, 10m), token, conversionId.ToString());

        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EconomyDbContext>();
        (await dbContext.ConversionRequests.AnyAsync(r => r.Id == conversionId, TestContext.CurrentContext.CancellationToken))
            .Should().BeFalse();
    }

    [Test]
    public async Task Create_NoIdempotencyKeyHeader_Returns400()
    {
        var (fromCurrencyId, toCurrencyId, _) = await SeedCurrencyPairAsync();
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid());

        var response = await PostAsync(new ConvertRequest(fromCurrencyId, toCurrencyId, 10m), token, idempotencyKey: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Create_MalformedIdempotencyKeyHeader_Returns400()
    {
        var (fromCurrencyId, toCurrencyId, _) = await SeedCurrencyPairAsync();
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid());

        var response = await PostAsync(new ConvertRequest(fromCurrencyId, toCurrencyId, 10m), token, "not-a-guid");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Create_RepeatedKeySameParameters_Returns202WithSameConversionId()
    {
        var (fromCurrencyId, toCurrencyId, _) = await SeedCurrencyPairAsync();
        var userId = Guid.NewGuid();
        var token = TestTokenFactory.IssueAccessToken(userId);
        await SeedPlatformBalanceAsync(userId, fromCurrencyId, 100m, "conversion-endpoint-seed-2");
        var idempotencyKey = Guid.NewGuid().ToString();
        var requestBody = new ConvertRequest(fromCurrencyId, toCurrencyId, 10m);

        var firstResponse = await PostAsync(requestBody, token, idempotencyKey);
        var secondResponse = await PostAsync(requestBody, token, idempotencyKey);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var firstBody = await firstResponse.Content.ReadFromJsonAsync<ConversionDto>(JsonOptions, TestContext.CurrentContext.CancellationToken);
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<ConversionDto>(JsonOptions, TestContext.CurrentContext.CancellationToken);
        secondBody!.ConversionId.Should().Be(firstBody!.ConversionId);
    }

    [Test]
    public async Task Create_RepeatedKeyDifferentParameters_Returns409()
    {
        var (fromCurrencyId, toCurrencyId, _) = await SeedCurrencyPairAsync();
        var userId = Guid.NewGuid();
        var token = TestTokenFactory.IssueAccessToken(userId);
        await SeedPlatformBalanceAsync(userId, fromCurrencyId, 100m, "conversion-endpoint-seed-3");
        var idempotencyKey = Guid.NewGuid().ToString();

        var firstResponse = await PostAsync(new ConvertRequest(fromCurrencyId, toCurrencyId, 10m), token, idempotencyKey);
        var secondResponse = await PostAsync(new ConvertRequest(fromCurrencyId, toCurrencyId, 20m), token, idempotencyKey);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.Accepted);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task Get_OtherUsersConversion_Returns404()
    {
        var (fromCurrencyId, toCurrencyId, _) = await SeedCurrencyPairAsync();
        var ownerId = Guid.NewGuid();
        var ownerToken = TestTokenFactory.IssueAccessToken(ownerId);
        await SeedPlatformBalanceAsync(ownerId, fromCurrencyId, 100m, "conversion-endpoint-seed-4");

        var createResponse = await PostAsync(
            new ConvertRequest(fromCurrencyId, toCurrencyId, 10m), ownerToken, Guid.NewGuid().ToString());
        var created = await createResponse.Content.ReadFromJsonAsync<ConversionDto>(JsonOptions, TestContext.CurrentContext.CancellationToken);

        var otherToken = TestTokenFactory.IssueAccessToken(Guid.NewGuid());
        var response = await GetAsync($"/conversions/{created!.ConversionId}", otherToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task Get_NonexistentId_Returns404()
    {
        var token = TestTokenFactory.IssueAccessToken(Guid.NewGuid());

        var response = await GetAsync($"/conversions/{Guid.NewGuid()}", token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<ConversionStatus> PollUntilTerminalAsync(Guid conversionId, string token)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            var response = await GetAsync($"/conversions/{conversionId}", token);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await response.Content.ReadFromJsonAsync<ConversionDto>(JsonOptions, TestContext.CurrentContext.CancellationToken);
            if (body!.Status is ConversionStatus.Completed or ConversionStatus.Failed)
            {
                return body.Status;
            }

            await Task.Delay(100, TestContext.CurrentContext.CancellationToken);
        }

        throw new TimeoutException($"Conversion {conversionId} did not reach a terminal status within the poll window.");
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string token)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);
    }

    private async Task<HttpResponseMessage> PostAsync(ConvertRequest requestBody, string token, string? idempotencyKey)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/conversions")
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

    private async Task SeedPlatformBalanceAsync(Guid userId, Guid currencyId, decimal amount, string idempotencyKey)
    {
        var adminToken = TestTokenFactory.IssueAccessToken(Guid.NewGuid(), role: "Admin");
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/transactions/grant")
        {
            Content = JsonContent.Create(new GrantRequest(userId, currencyId, amount, "conversion test seed"), options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        var response = await client.SendAsync(request, TestContext.CurrentContext.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private async Task<(Guid FromCurrencyId, Guid ToCurrencyId, decimal Rate)> SeedCurrencyPairAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EconomyDbContext>();

        var platformCurrency = new Currency
        {
            Id = Guid.CreateVersion7(),
            Code = $"PLATFORM_{Guid.NewGuid():N}",
            DisplayName = "Test Platform Credits",
            Scope = CurrencyScope.Platform,
            GameId = null,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var gameCurrency = new Currency
        {
            Id = Guid.CreateVersion7(),
            Code = $"GAME_{Guid.NewGuid():N}",
            DisplayName = "Test Game Gold",
            Scope = CurrencyScope.Game,
            GameId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        const decimal rate = 100m;
        dbContext.Currencies.AddRange(platformCurrency, gameCurrency);
        dbContext.ConversionRates.Add(new ConversionRate
        {
            Id = Guid.CreateVersion7(),
            FromCurrencyId = platformCurrency.Id,
            ToCurrencyId = gameCurrency.Id,
            Rate = rate,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await dbContext.SaveChangesAsync(TestContext.CurrentContext.CancellationToken);

        return (platformCurrency.Id, gameCurrency.Id, rate);
    }
}
