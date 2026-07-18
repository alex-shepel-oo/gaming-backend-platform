using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using IdentityService.Contracts.Requests;
using IdentityService.Contracts.Responses;
using IdentityService.Domain;
using IdentityService.Persistence;
using IdentityService.Services;
using IdentityService.Services.Email;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests.Integration;

[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed partial class ConfirmEmailTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [GeneratedRegex(@"\d{6}")]
    private static partial Regex SixDigitCode();

    [Fact]
    public async Task Confirm_WithinTtl_Returns204AndMarksUserConfirmed()
    {
        using var client = factory.CreateClient();
        var (game, email, _) = await RegisterAsync(client);
        var code = ExtractCode(factory.EmailSender.Sent.Single());

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/confirm-email",
            new ConfirmEmailRequest(email, code),
            JsonOptions,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var user = await FindUserAsync(email);
        user!.EmailConfirmed.Should().BeTrue();
        user.EmailConfirmedAt.Should().NotBeNull();

        _ = game;
    }

    [Fact]
    public async Task Confirm_AfterTtl_Returns400()
    {
        using var client = factory.CreateClient();
        var (_, email, _) = await RegisterAsync(client);
        var code = ExtractCode(factory.EmailSender.Sent.Single());

        factory.TimeProvider.Advance(TimeSpan.FromMinutes(21));

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/confirm-email",
            new ConfirmEmailRequest(email, code),
            JsonOptions,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var user = await FindUserAsync(email);
        user!.EmailConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_ExpiredCode_DoesNotIncrementAttempts()
    {
        using var client = factory.CreateClient();
        var (_, email, _) = await RegisterAsync(client);
        var code = ExtractCode(factory.EmailSender.Sent.Single());
        var user = await FindUserAsync(email);
        var codeRow = await FindActiveCodeAsync(user!.Id);

        factory.TimeProvider.Advance(TimeSpan.FromMinutes(21));

        await client.PostAsJsonAsync(
            "/api/identity/auth/confirm-email",
            new ConfirmEmailRequest(email, code),
            JsonOptions,
            TestContext.Current.CancellationToken);

        var reloaded = await FindCodeAsync(codeRow!.Id);
        reloaded!.AttemptCount.Should().Be(0);
    }

    [Fact]
    public async Task Confirm_AlreadyUsedCode_Returns400()
    {
        using var client = factory.CreateClient();
        var (_, email, _) = await RegisterAsync(client);
        var code = ExtractCode(factory.EmailSender.Sent.Single());

        var first = await client.PostAsJsonAsync(
            "/api/identity/auth/confirm-email",
            new ConfirmEmailRequest(email, code),
            JsonOptions,
            TestContext.Current.CancellationToken);
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await client.PostAsJsonAsync(
            "/api/identity/auth/confirm-email",
            new ConfirmEmailRequest(email, code),
            JsonOptions,
            TestContext.Current.CancellationToken);
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var user = await FindUserAsync(email);
        user!.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task Confirm_CodeSupersededByNewIssue_Returns400()
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var userId = await SeedUnconfirmedUserAsync(email);

        await using var scope = factory.Services.CreateAsyncScope();
        var emailVerificationService = scope.ServiceProvider.GetRequiredService<IEmailVerificationService>();

        var first = await emailVerificationService.IssueCodeAsync(
            userId, gameId: null, email, TestContext.Current.CancellationToken);
        await emailVerificationService.IssueCodeAsync(
            userId, gameId: null, email, TestContext.Current.CancellationToken);

        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/confirm-email",
            new ConfirmEmailRequest(email, first.RawCode),
            JsonOptions,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Confirm_WrongCodeFiveTimes_KillsCode()
    {
        using var client = factory.CreateClient();
        var (_, email, _) = await RegisterAsync(client);
        var correctCode = ExtractCode(factory.EmailSender.Sent.Single());
        var wrongCode = correctCode == "000000" ? "000001" : "000000";

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var attemptResponse = await client.PostAsJsonAsync(
                "/api/identity/auth/confirm-email",
                new ConfirmEmailRequest(email, wrongCode),
                JsonOptions,
                TestContext.Current.CancellationToken);
            attemptResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        var finalResponse = await client.PostAsJsonAsync(
            "/api/identity/auth/confirm-email",
            new ConfirmEmailRequest(email, correctCode),
            JsonOptions,
            TestContext.Current.CancellationToken);

        finalResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var user = await FindUserAsync(email);
        user!.EmailConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_UnknownEmail_ReturnsSameBodyAsWrongCode()
    {
        using var client = factory.CreateClient();
        var (_, email, _) = await RegisterAsync(client);
        var correctCode = ExtractCode(factory.EmailSender.Sent.Single());
        var wrongCode = correctCode == "000000" ? "000001" : "000000";

        var wrongCodeResponse = await client.PostAsJsonAsync(
            "/api/identity/auth/confirm-email",
            new ConfirmEmailRequest(email, wrongCode),
            JsonOptions,
            TestContext.Current.CancellationToken);

        var unknownEmailResponse = await client.PostAsJsonAsync(
            "/api/identity/auth/confirm-email",
            new ConfirmEmailRequest($"{Guid.NewGuid():N}@example.com", wrongCode),
            JsonOptions,
            TestContext.Current.CancellationToken);

        wrongCodeResponse.StatusCode.Should().Be(unknownEmailResponse.StatusCode);

        var wrongCodeBody = await NormalizedProblemBodyAsync(wrongCodeResponse);
        var unknownEmailBody = await NormalizedProblemBodyAsync(unknownEmailResponse);

        wrongCodeBody.Should().Be(unknownEmailBody);
    }

    [Fact]
    public async Task Confirm_CodeIssuedDuringRegistrationInGameA_ConfirmsAccountGloballyForGameB()
    {
        using var client = factory.CreateClient();
        var (gameA, email, _) = await RegisterAsync(client);
        var code = ExtractCode(factory.EmailSender.Sent.Single());

        var confirmResponse = await client.PostAsJsonAsync(
            "/api/identity/auth/confirm-email",
            new ConfirmEmailRequest(email, code),
            JsonOptions,
            TestContext.Current.CancellationToken);
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        factory.EmailSender.Clear();
        var gameB = await SeedGameAsync();

        var secondRegisterResponse = await client.PostAsJsonAsync(
            "/api/identity/auth/register",
            new RegisterRequest(gameB.Slug, email, "Player One", "correct-horse-battery"),
            JsonOptions,
            TestContext.Current.CancellationToken);

        var body = await secondRegisterResponse.Content.ReadFromJsonAsync<RegistrationAcceptedResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        body!.VerificationRequired.Should().BeFalse();
        factory.EmailSender.Sent.Should().BeEmpty();

        _ = gameA;
    }

    private static string ExtractCode(EmailMessage message) => SixDigitCode().Match(message.HtmlBody).Value;

    private static async Task<string> NormalizedProblemBodyAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var properties = json.EnumerateObject()
            .Where(p => p.Name != "traceId")
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.Value}");

        return string.Join('&', properties);
    }

    private async Task<(Game Game, string Email, Guid UserId)> RegisterAsync(HttpClient client)
    {
        var game = await SeedGameAsync();
        var email = $"{Guid.NewGuid():N}@example.com";

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/register",
            new RegisterRequest(game.Slug, email, "Player One", "correct-horse-battery"),
            JsonOptions,
            TestContext.Current.CancellationToken);

        var body = await response.Content.ReadFromJsonAsync<RegistrationAcceptedResponse>(
            JsonOptions, TestContext.Current.CancellationToken);

        return (game, email, body!.UserId);
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

    private async Task<Guid> SeedUnconfirmedUserAsync(string email)
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
            EmailConfirmed = false,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return user.Id;
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
