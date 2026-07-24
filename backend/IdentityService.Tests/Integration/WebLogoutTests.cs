using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using IdentityService.Auth;
using IdentityService.Contracts.Requests;
using IdentityService.Domain;
using IdentityService.Domain.Enums;
using IdentityService.Persistence;
using IdentityService.Services;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace IdentityService.Tests.Integration;

[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed class WebLogoutTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private const string Password = "correct-horse-battery";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task WebLogout_ValidSession_Returns204ClearsCookieAndRevokesFamily()
    {
        var (gameSlug, email) = await SeedPlayerAsync();
        using var client = CreateClient();

        var (accessToken, refreshCookie) = await LoginWebAsync(client, gameSlug, email);

        var logoutResponse = await LogoutWebAsync(client, accessToken, refreshCookie);

        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var clearedCookie = SetCookieHeaderValue.Parse(logoutResponse.Headers.GetValues("Set-Cookie").Single());
        clearedCookie.Name.ToString().Should().Be("gbp_refresh");
        clearedCookie.MaxAge.Should().Be(TimeSpan.Zero);
        clearedCookie.Path.ToString().Should().Be("/api/identity/auth");

        var refreshAfterLogout = await RefreshWebAsync(client, refreshCookie);
        refreshAfterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WebLogout_AlreadyRevokedFamilyPresentedAgainByAFreshSession_IsANoOp()
    {
        var (gameSlug, email) = await SeedPlayerAsync();
        using var client = CreateClient();

        var (firstAccessToken, firstRefreshCookie) = await LoginWebAsync(client, gameSlug, email);
        await LogoutWebAsync(client, firstAccessToken, firstRefreshCookie);

        var (secondAccessToken, _) = await LoginWebAsync(client, gameSlug, email);

        var secondLogout = await LogoutWebAsync(client, secondAccessToken, firstRefreshCookie);

        secondLogout.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private HttpClient CreateClient() => factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static async Task<(string AccessToken, string RefreshCookie)> LoginWebAsync(HttpClient client, string gameSlug, string email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(gameSlug, email, Password), options: JsonOptions),
        };
        request.Headers.Add(ClientMode.HeaderName, "web");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var accessToken = json.GetProperty("accessToken").GetString()!;
        var refreshCookie = SetCookieHeaderValue.Parse(response.Headers.GetValues("Set-Cookie").Single()).Value.ToString();

        return (accessToken, refreshCookie);
    }

    private static async Task<HttpResponseMessage> LogoutWebAsync(HttpClient client, string accessToken, string? cookieValue)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/auth/logout");
        request.Headers.Add(ClientMode.HeaderName, "web");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (cookieValue is not null)
        {
            request.Headers.Add("Cookie", $"gbp_refresh={cookieValue}");
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> RefreshWebAsync(HttpClient client, string cookieValue)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/auth/refresh");
        request.Headers.Add(ClientMode.HeaderName, "web");
        request.Headers.Add("Cookie", $"gbp_refresh={cookieValue}");

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<(string GameSlug, string Email)> SeedPlayerAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = factory.TimeProvider.GetUtcNow();

        var game = new Game
        {
            Id = Guid.CreateVersion7(),
            Slug = $"game-{Guid.NewGuid():N}",
            Name = "Test Game",
            IsActive = true,
            CreatedAt = now,
        };

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = $"{Guid.NewGuid():N}@example.com",
            DisplayName = "Test Player",
            PasswordHash = passwordHasher.Hash(Password),
            IsActive = true,
            EmailConfirmed = true,
            EmailConfirmedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.Games.Add(game);
        dbContext.Users.Add(user);
        dbContext.UserGameRoles.Add(new UserGameRole
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            GameId = game.Id,
            Role = PlatformRole.Player,
            GrantedAt = now,
        });

        await dbContext.SaveChangesAsync();

        return (game.Slug, user.Email);
    }
}
