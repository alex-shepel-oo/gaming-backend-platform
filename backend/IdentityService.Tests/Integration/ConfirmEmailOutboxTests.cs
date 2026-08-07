using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using BuildingBlocks.Messaging.Outbox;
using IdentityService.Contracts.Requests;
using IdentityService.Contracts.Responses;
using IdentityService.Domain;
using IdentityService.Messaging.Events;
using IdentityService.Persistence;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests.Integration;

[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed class ConfirmEmailOutboxTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Confirm_Success_WritesOneUnprocessedOutboxRowWithConfirmedUserId()
    {
        using var client = factory.CreateClient();
        var (email, userId) = await RegisterAsync(client);
        var code = await ExtractCodeAsync(email);

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/confirm-email",
            new ConfirmEmailRequest(email, code),
            JsonOptions,
            TestContext.Current.CancellationToken);

        response.EnsureSuccessStatusCode();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var outboxMessage = await dbContext.Set<OutboxMessage>()
            .SingleAsync(m => m.Type == "user.email_confirmed", TestContext.Current.CancellationToken);

        outboxMessage.ProcessedAt.Should().BeNull();

        var payload = JsonSerializer.Deserialize<UserEmailConfirmedEvent>(outboxMessage.Payload)!;
        payload.UserId.Should().Be(userId);
    }

    [Fact]
    public async Task Confirm_WrongCode_DoesNotWriteUserEmailConfirmedOutboxRow()
    {
        using var client = factory.CreateClient();
        var (email, _) = await RegisterAsync(client);
        var correctCode = await ExtractCodeAsync(email);
        var wrongCode = correctCode == "000000" ? "000001" : "000000";

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/confirm-email",
            new ConfirmEmailRequest(email, wrongCode),
            JsonOptions,
            TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.Should().BeFalse();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        // Registration itself already wrote an email_verification.requested row above: the
        // assertion here is specifically that a wrong code never produces a user.email_confirmed
        // one, not that the outbox table stays empty.
        (await dbContext.Set<OutboxMessage>().AnyAsync(m => m.Type == "user.email_confirmed", TestContext.Current.CancellationToken))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_ExpiredCode_DoesNotWriteUserEmailConfirmedOutboxRow()
    {
        using var client = factory.CreateClient();
        var (email, _) = await RegisterAsync(client);
        var code = await ExtractCodeAsync(email);

        factory.TimeProvider.Advance(TimeSpan.FromMinutes(21));

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/confirm-email",
            new ConfirmEmailRequest(email, code),
            JsonOptions,
            TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.Should().BeFalse();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        (await dbContext.Set<OutboxMessage>().AnyAsync(m => m.Type == "user.email_confirmed", TestContext.Current.CancellationToken))
            .Should().BeFalse();
    }

    private async Task<string> ExtractCodeAsync(string email)
    {
        var events = await factory.GetOutboxEventsAsync<EmailVerificationRequestedEvent>(
            "email_verification.requested", TestContext.Current.CancellationToken);

        return events.Single(e => e.Email == email).Code;
    }

    private async Task<(string Email, Guid UserId)> RegisterAsync(HttpClient client)
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

        return (email, body!.UserId);
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
}
