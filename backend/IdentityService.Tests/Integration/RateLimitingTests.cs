using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using IdentityService.Contracts.Requests;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace IdentityService.Tests.Integration;

[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed class RateLimitingTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Login_ExceedingIpLimit_Returns429WithRetryAfter()
    {
        using var scopedFactory = LowLimitFactory("Login");
        using var client = scopedFactory.CreateClient();

        HttpResponseMessage response = null!;
        for (var i = 0; i < 3; i++)
        {
            response = await client.PostAsJsonAsync(
                "/api/identity/auth/login",
                new LoginRequest("no-such-game", "nobody@example.com", "wrong-password"),
                JsonOptions,
                TestContext.Current.CancellationToken);
        }

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task Register_ExceedingIpLimit_Returns429()
    {
        using var scopedFactory = LowLimitFactory("Register");
        using var client = scopedFactory.CreateClient();

        HttpResponseMessage response = null!;
        for (var i = 0; i < 3; i++)
        {
            response = await client.PostAsJsonAsync(
                "/api/identity/auth/register",
                new RegisterRequest($"no-such-game-{i}", $"{Guid.NewGuid():N}@example.com", "Someone Player", "a-long-enough-password"),
                JsonOptions,
                TestContext.Current.CancellationToken);
        }

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ConfirmEmail_ExceedingIpLimit_Returns429()
    {
        using var scopedFactory = LowLimitFactory("ConfirmEmail");
        using var client = scopedFactory.CreateClient();

        HttpResponseMessage response = null!;
        for (var i = 0; i < 3; i++)
        {
            response = await client.PostAsJsonAsync(
                "/api/identity/auth/confirm-email",
                new ConfirmEmailRequest($"{Guid.NewGuid():N}@example.com", "000000"),
                JsonOptions,
                TestContext.Current.CancellationToken);
        }

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task ResendVerification_ExceedingIpLimit_Returns429()
    {
        using var scopedFactory = LowLimitFactory("ResendVerification");
        using var client = scopedFactory.CreateClient();

        HttpResponseMessage response = null!;
        for (var i = 0; i < 3; i++)
        {
            response = await client.PostAsJsonAsync(
                "/api/identity/auth/resend-verification",
                new ResendVerificationRequest($"{Guid.NewGuid():N}@example.com", GameSlug: null),
                JsonOptions,
                TestContext.Current.CancellationToken);
        }

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    private WebApplicationFactory<Program> LowLimitFactory(string partition) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(
            (_, configBuilder) => configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"RateLimiting:{partition}PermitLimit"] = "2",
                [$"RateLimiting:{partition}WindowSeconds"] = "2",
            })));
}
