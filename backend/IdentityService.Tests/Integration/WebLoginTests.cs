using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using IdentityService.Auth;
using IdentityService.Contracts.Requests;
using IdentityService.Contracts.Responses;
using IdentityService.Domain;
using IdentityService.Domain.Enums;
using IdentityService.Persistence;
using IdentityService.Services;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace IdentityService.Tests.Integration;

[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed class WebLoginTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private const string Password = "correct-horse-battery";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonWebTokenHandler TokenHandler = new();

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Login_WebMode_Returns200WithAccessOnlyBodyAndRefreshCookie()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, game.Slug, user.Email, Password, webMode: true);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        json.TryGetProperty("refreshToken", out _).Should().BeFalse();

        var setCookie = SetCookieHeaderValue.Parse(response.Headers.GetValues("Set-Cookie").Single());
        setCookie.Name.ToString().Should().Be("gbp_refresh");
        setCookie.HttpOnly.Should().BeTrue();
        setCookie.Path.ToString().Should().Be("/api/identity/auth");
    }

    [Fact]
    public async Task Login_BodyMode_ReturnsTokenPairAndNoCookie()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, game.Slug, user.Email, Password, webMode: false);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<TokenPairResponse>(JsonOptions, TestContext.Current.CancellationToken);
        body!.AccessToken.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBeNullOrEmpty();

        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task Login_AdminClientType_ReturnsAdminAudienceTokenAndAdminRefreshCookie()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var response = await LoginWithClientTypeAsync(client, game.Slug, user.Email, Password, "admin");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var jwt = TokenHandler.ReadJsonWebToken(json.GetProperty("accessToken").GetString());
        jwt.Audiences.Should().ContainSingle().Which.Should().Be(TokenAudiences.Admin);

        var setCookie = SetCookieHeaderValue.Parse(response.Headers.GetValues("Set-Cookie").Single());
        setCookie.Name.ToString().Should().Be("gbp_admin_refresh");
    }

    [Fact]
    public async Task Login_WebClientType_ReturnsPlayerAudienceTokenAndPlayerRefreshCookie()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var response = await LoginWithClientTypeAsync(client, game.Slug, user.Email, Password, "web");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var jwt = TokenHandler.ReadJsonWebToken(json.GetProperty("accessToken").GetString());
        jwt.Audiences.Should().ContainSingle().Which.Should().Be(TokenAudiences.Player);

        var setCookie = SetCookieHeaderValue.Parse(response.Headers.GetValues("Set-Cookie").Single());
        setCookie.Name.ToString().Should().Be("gbp_refresh");
    }

    [Fact]
    public async Task Login_WebMode_UnconfirmedUser_Returns403EmailNotConfirmed()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: false, active: true);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, game.Slug, user.Email, Password, webMode: true);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("type").GetString().Should().Be("https://gaming-backend-platform/problems/email-not-confirmed");
    }

    [Fact]
    public async Task Login_WebMode_WrongPassword_Returns401WithoutSettingCookie()
    {
        var game = await SeedGameAsync();
        var user = await SeedUserAsync(game.Id, confirmed: true, active: true);
        using var client = factory.CreateClient();

        var response = await LoginAsync(client, game.Slug, user.Email, "wrong-password-entirely", webMode: true);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    private static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client, string? gameSlug, string email, string password, bool webMode)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(gameSlug, email, password), options: JsonOptions),
        };

        if (webMode)
        {
            request.Headers.Add(ClientMode.HeaderName, "web");
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> LoginWithClientTypeAsync(
        HttpClient client, string? gameSlug, string email, string password, string clientType)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(gameSlug, email, password), options: JsonOptions),
        };

        request.Headers.Add(ClientMode.HeaderName, clientType);

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<Game> SeedGameAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = factory.TimeProvider.GetUtcNow();

        var game = new Game
        {
            Id = Guid.CreateVersion7(),
            Slug = $"game-{Guid.NewGuid():N}",
            Name = "Test Game",
            IsActive = true,
            CreatedAt = now,
        };

        dbContext.Games.Add(game);
        await dbContext.SaveChangesAsync();

        return game;
    }

    private async Task<User> SeedUserAsync(Guid gameId, bool confirmed, bool active)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = factory.TimeProvider.GetUtcNow();

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = $"{Guid.NewGuid():N}@example.com",
            DisplayName = "Test Player",
            PasswordHash = passwordHasher.Hash(Password),
            IsActive = active,
            EmailConfirmed = confirmed,
            EmailConfirmedAt = confirmed ? now : null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.Users.Add(user);
        dbContext.UserGameRoles.Add(new UserGameRole
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            GameId = gameId,
            Role = PlatformRole.Player,
            GrantedAt = now,
        });

        await dbContext.SaveChangesAsync();

        return user;
    }
}
