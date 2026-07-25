using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using IdentityService.Contracts.Requests;
using IdentityService.Contracts.Responses;
using IdentityService.Domain;
using IdentityService.Domain.Enums;
using IdentityService.Persistence;
using IdentityService.Services;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests.Integration;

[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed partial class RegisterEndpointTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [GeneratedRegex(@"\d{6}")]
    private static partial Regex SixDigitCode();

    [Fact]
    public async Task Register_NewEmail_Returns202WithNoTokensAndIssuesCode()
    {
        var game = await SeedGameAsync();
        var email = $"{Guid.NewGuid():N}@example.com";
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/register",
            new RegisterRequest(game.Slug, email, "Player One", "correct-horse-battery"),
            JsonOptions,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location.Should().BeNull();

        var body = await response.Content.ReadFromJsonAsync<RegistrationAcceptedResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        body.Should().NotBeNull();
        body!.Email.Should().Be(email);
        body.VerificationRequired.Should().BeTrue();
        body.CodeExpiresAt.Should().NotBeNull();

        var responseText = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        responseText.Should().NotContain("accessToken");
        responseText.Should().NotContain("refreshToken");

        var user = await FindUserAsync(email);
        user.Should().NotBeNull();
        user!.EmailConfirmed.Should().BeFalse();

        var role = await FindRoleAsync(user.Id, game.Id);
        role.Should().NotBeNull();
        role!.Role.Should().Be(PlatformRole.Player);

        var code = await FindActiveCodeAsync(user.Id);
        code.Should().NotBeNull();
        code!.ConsumedAt.Should().BeNull();
        code.GameId.Should().Be(game.Id);

        var familyCount = await CountFamiliesAsync(user.Id);
        familyCount.Should().Be(0);

        factory.EmailSender.Sent.Should().HaveCount(1);
        var sent = factory.EmailSender.Sent.Single();
        sent.To.Should().Be(email);
        sent.Subject.Should().NotContain(SixDigitCode().Match(sent.HtmlBody).Value);

        var rawCode = SixDigitCode().Match(sent.HtmlBody).Value;
        await using var scope = factory.Services.CreateAsyncScope();
        var verificationCodeGenerator = scope.ServiceProvider.GetRequiredService<IVerificationCodeGenerator>();
        verificationCodeGenerator.Verify(rawCode, code.CodeHash).Should().BeTrue();
    }

    [Fact]
    public async Task Register_ConfirmedEmailInSecondGame_SkipsVerification()
    {
        var firstGame = await SeedGameAsync();
        var secondGame = await SeedGameAsync();
        var email = $"{Guid.NewGuid():N}@example.com";
        var userId = await SeedConfirmedUserAsync(email, firstGame.Id);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/register",
            new RegisterRequest(secondGame.Slug, email, "Player One", "correct-horse-battery"),
            JsonOptions,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await response.Content.ReadFromJsonAsync<RegistrationAcceptedResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        body!.VerificationRequired.Should().BeFalse();
        body.CodeExpiresAt.Should().BeNull();

        var role = await FindRoleAsync(userId, secondGame.Id);
        role.Should().NotBeNull();

        var code = await FindActiveCodeAsync(userId);
        code.Should().BeNull();

        factory.EmailSender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Register_UnconfirmedEmailInSecondGame_AddsRoleAndReissuesCode()
    {
        var firstGame = await SeedGameAsync();
        var secondGame = await SeedGameAsync();
        var email = $"{Guid.NewGuid():N}@example.com";
        using var client = factory.CreateClient();

        await client.PostAsJsonAsync(
            "/api/identity/auth/register",
            new RegisterRequest(firstGame.Slug, email, "Player One", "correct-horse-battery"),
            JsonOptions,
            TestContext.Current.CancellationToken);

        var user = await FindUserAsync(email);
        var firstCode = await FindActiveCodeAsync(user!.Id);

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/register",
            new RegisterRequest(secondGame.Slug, email, "Player One", "correct-horse-battery"),
            JsonOptions,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var body = await response.Content.ReadFromJsonAsync<RegistrationAcceptedResponse>(
            JsonOptions, TestContext.Current.CancellationToken);
        body!.VerificationRequired.Should().BeTrue();

        var roleInFirstGame = await FindRoleAsync(user.Id, firstGame.Id);
        var roleInSecondGame = await FindRoleAsync(user.Id, secondGame.Id);
        roleInFirstGame.Should().NotBeNull();
        roleInSecondGame.Should().NotBeNull();

        var oldCode = await FindCodeAsync(firstCode!.Id);
        oldCode!.ConsumedAt.Should().NotBeNull();

        var activeCode = await FindActiveCodeAsync(user.Id);
        activeCode.Should().NotBeNull();
        activeCode!.Id.Should().NotBe(firstCode.Id);

        factory.EmailSender.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task Register_ConfirmedEmailAlreadyHasRoleInGame_Returns202()
    {
        var game = await SeedGameAsync();
        var email = $"{Guid.NewGuid():N}@example.com";
        var userId = await SeedConfirmedUserAsync(email, game.Id);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/register",
            new RegisterRequest(game.Slug, email, "Player One", "correct-horse-battery"),
            JsonOptions,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var roles = await dbContext.UserGameRoles
            .Where(r => r.UserId == userId && r.GameId == game.Id)
            .ToListAsync(TestContext.Current.CancellationToken);
        roles.Should().ContainSingle();

        factory.EmailSender.Sent.Should().HaveCount(1);
        factory.EmailSender.Sent.Single().To.Should().Be(email);
    }

    [Fact]
    public async Task Register_UnknownGameSlug_Returns404()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/register",
            new RegisterRequest($"no-such-game-{Guid.NewGuid():N}", "player@example.com", "Player One", "correct-horse-battery"),
            JsonOptions,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<Game> SeedGameAsync(bool isActive = true)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = factory.TimeProvider.GetUtcNow();

        var game = new Game
        {
            Id = Guid.CreateVersion7(),
            Slug = $"game-{Guid.NewGuid():N}",
            Name = "Test Game",
            IsActive = isActive,
            CreatedAt = now,
        };

        dbContext.Games.Add(game);
        await dbContext.SaveChangesAsync();

        return game;
    }

    private async Task<Guid> SeedConfirmedUserAsync(string email, Guid gameId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = factory.TimeProvider.GetUtcNow();

        var user = new User
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

        return user.Id;
    }

    private async Task<User?> FindUserAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.Users.SingleOrDefaultAsync(u => u.Email == email);
    }

    private async Task<UserGameRole?> FindRoleAsync(Guid userId, Guid gameId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.UserGameRoles.SingleOrDefaultAsync(r => r.UserId == userId && r.GameId == gameId);
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

    private async Task<int> CountFamiliesAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.RefreshTokenFamilies.CountAsync(f => f.UserId == userId);
    }
}
