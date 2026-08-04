using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using IdentityService.Auth;
using IdentityService.Contracts.Requests;
using IdentityService.Contracts.Responses;
using IdentityService.Domain.Enums;
using IdentityService.Persistence;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests.Integration;

// Exercises the dev-seeded demo-shooter/demo-racer tenants themselves (unlike
// TenantIsolationTests, which seeds its own throwaway games per test) - the point is to
// prove the seeder's data is actually usable end to end, not just present in the database.
[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed class DevSeedTenantsTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private const string SeedPassword = "DemoPassword123!";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>();
        await seeder.SeedAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task DemoRacerAdmin_ManagesOwnGamesModeratorPermissions_ButNotDemoShootersReturns200Then403()
    {
        var (client, accessToken) = await LoginAsync("gameadmin@demo-racer.dev", "demo-racer");
        var demoRacerId = await GameIdAsync("demo-racer");
        var demoShooterId = await GameIdAsync("demo-shooter");

        var ownPermissions = new[]
        {
            Permissions.GameMetadataEdit,
            Permissions.GameCurrencyManage,
            Permissions.GameBalanceAdjust,
            Permissions.GameRolesManage,
            Permissions.GamePlayersModerate,
        };

        var ownGameResponse = await PutAuthorizedAsync(
            client, $"/api/identity/roles/{PlatformRole.Moderator}/permissions?gameId={demoRacerId}",
            new UpdateRolePermissionsRequest(ownPermissions), accessToken);

        ownGameResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var otherGameResponse = await PutAuthorizedAsync(
            client, $"/api/identity/roles/{PlatformRole.Moderator}/permissions?gameId={demoShooterId}",
            new UpdateRolePermissionsRequest(ownPermissions), accessToken);

        otherGameResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListPublicGames_IncludesDemoRacer()
    {
        var (client, accessToken) = await LoginAsync("gameadmin@demo-racer.dev", "demo-racer");

        var response = await GetAuthorizedAsync(client, "/api/identity/games/public", accessToken);
        var games = await response.Content.ReadFromJsonAsync<PublicGameDto[]>(JsonOptions, TestContext.Current.CancellationToken);

        games.Should().Contain(g => g.Slug == "demo-racer");
    }

    private async Task<Guid> GameIdAsync(string slug)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return (await dbContext.Games.SingleAsync(g => g.Slug == slug)).Id;
    }

    private static Task<HttpResponseMessage> GetAuthorizedAsync(HttpClient client, string url, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> PutAuthorizedAsync<TBody>(HttpClient client, string url, TBody body, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private async Task<(HttpClient Client, string AccessToken)> LoginAsync(string email, string gameSlug)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/login",
            new LoginRequest(gameSlug, email, SeedPassword),
            JsonOptions,
            TestContext.Current.CancellationToken);

        var tokens = await response.Content.ReadFromJsonAsync<TokenPairResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        return (client, tokens!.AccessToken);
    }
}
