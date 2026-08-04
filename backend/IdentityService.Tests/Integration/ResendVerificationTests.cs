using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using IdentityService.Contracts.Requests;
using IdentityService.Domain;
using IdentityService.Messaging.Events;
using IdentityService.Persistence;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests.Integration;

[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed class ResendVerificationTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Resend_WithinCooldown_DoesNotSendSecondEmail()
    {
        using var client = factory.CreateClient();
        var (game, email) = await RegisterAsync(client);

        var first = await ResendAsync(client, email, game.Slug);
        var second = await ResendAsync(client, email, game.Slug);

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);

        (await factory.GetOutboxEventsAsync<EmailVerificationRequestedEvent>(
            "email_verification.requested", TestContext.Current.CancellationToken)).Should().ContainSingle();
    }

    [Fact]
    public async Task Resend_AfterCooldown_SendsAgain()
    {
        using var client = factory.CreateClient();
        var (game, email) = await RegisterAsync(client);

        factory.TimeProvider.Advance(TimeSpan.FromSeconds(61));
        var response = await ResendAsync(client, email, game.Slug);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await factory.GetOutboxEventsAsync<EmailVerificationRequestedEvent>(
            "email_verification.requested", TestContext.Current.CancellationToken)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Resend_SixthInAnHour_DoesNotSend()
    {
        using var client = factory.CreateClient();
        var (game, email) = await RegisterAsync(client);

        for (var i = 0; i < 4; i++)
        {
            factory.TimeProvider.Advance(TimeSpan.FromSeconds(61));
            await ResendAsync(client, email, game.Slug);
        }

        (await factory.GetOutboxEventsAsync<EmailVerificationRequestedEvent>(
            "email_verification.requested", TestContext.Current.CancellationToken)).Should().HaveCount(5);

        factory.TimeProvider.Advance(TimeSpan.FromSeconds(61));
        var sixthAttempt = await ResendAsync(client, email, game.Slug);

        sixthAttempt.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await factory.GetOutboxEventsAsync<EmailVerificationRequestedEvent>(
            "email_verification.requested", TestContext.Current.CancellationToken)).Should().HaveCount(5);
    }

    [Fact]
    public async Task Resend_UnknownEmail_Returns202AndSendsNothing()
    {
        using var client = factory.CreateClient();

        var response = await ResendAsync(client, $"{Guid.NewGuid():N}@example.com", gameSlug: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await factory.GetOutboxEventsAsync<EmailVerificationRequestedEvent>(
            "email_verification.requested", TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task Resend_AlreadyConfirmedEmail_Returns202AndSendsNothing()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        await SeedConfirmedUserAsync(email);
        using var client = factory.CreateClient();

        var response = await ResendAsync(client, email, gameSlug: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await factory.GetOutboxEventsAsync<EmailVerificationRequestedEvent>(
            "email_verification.requested", TestContext.Current.CancellationToken)).Should().BeEmpty();
    }

    [Fact]
    public async Task Resend_AfterCooldown_SupersedesPreviousCode()
    {
        using var client = factory.CreateClient();
        var (game, email) = await RegisterAsync(client);
        var user = await FindUserAsync(email);
        var firstCode = await FindActiveCodeAsync(user!.Id);

        factory.TimeProvider.Advance(TimeSpan.FromSeconds(61));
        await ResendAsync(client, email, game.Slug);

        var reloadedFirst = await FindCodeAsync(firstCode!.Id);
        reloadedFirst!.ConsumedAt.Should().NotBeNull();

        var activeCode = await FindActiveCodeAsync(user.Id);
        activeCode.Should().NotBeNull();
        activeCode!.Id.Should().NotBe(firstCode.Id);
    }

    private static Task<HttpResponseMessage> ResendAsync(HttpClient client, string email, string? gameSlug) =>
        client.PostAsJsonAsync(
            "/api/identity/auth/resend-verification",
            new ResendVerificationRequest(email, gameSlug),
            JsonOptions,
            TestContext.Current.CancellationToken);

    private async Task<(Game Game, string Email)> RegisterAsync(HttpClient client)
    {
        var game = await SeedGameAsync();
        var email = $"{Guid.NewGuid():N}@example.com";

        await client.PostAsJsonAsync(
            "/api/identity/auth/register",
            new RegisterRequest(game.Slug, email, "Player One", "correct-horse-battery"),
            JsonOptions,
            TestContext.Current.CancellationToken);

        return (game, email);
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

    private async Task SeedConfirmedUserAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = factory.TimeProvider.GetUtcNow();

        dbContext.Users.Add(new User
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            DisplayName = "Test Player",
            PasswordHash = "irrelevant",
            IsActive = true,
            EmailConfirmed = true,
            EmailConfirmedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await dbContext.SaveChangesAsync();
    }

    private async Task<User?> FindUserAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.Users.SingleOrDefaultAsync(u => u.Email == email);
    }

    private async Task<EmailVerificationCode?> FindActiveCodeAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.EmailVerificationCodes
            .SingleOrDefaultAsync(c => c.UserId == userId && c.ConsumedAt == null);
    }

    private async Task<EmailVerificationCode?> FindCodeAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.EmailVerificationCodes.SingleOrDefaultAsync(c => c.Id == id);
    }
}
