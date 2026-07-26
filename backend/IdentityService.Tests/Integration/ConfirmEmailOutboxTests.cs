using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using BuildingBlocks.Messaging.Outbox;
using IdentityService.Contracts.Requests;
using IdentityService.Contracts.Responses;
using IdentityService.Domain;
using IdentityService.Messaging.Events;
using IdentityService.Persistence;
using IdentityService.Services.Email;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests.Integration;

[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed partial class ConfirmEmailOutboxTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [GeneratedRegex(@"\d{6}")]
    private static partial Regex SixDigitCode();

    [Fact]
    public async Task Confirm_Success_WritesOneUnprocessedOutboxRowWithConfirmedUserId()
    {
        using var client = factory.CreateClient();
        var (email, userId) = await RegisterAsync(client);
        var code = ExtractCode(factory.EmailSender.Sent.Single());

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
    public async Task Confirm_WrongCode_DoesNotWriteOutboxRow()
    {
        using var client = factory.CreateClient();
        var (email, _) = await RegisterAsync(client);
        var correctCode = ExtractCode(factory.EmailSender.Sent.Single());
        var wrongCode = correctCode == "000000" ? "000001" : "000000";

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/confirm-email",
            new ConfirmEmailRequest(email, wrongCode),
            JsonOptions,
            TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.Should().BeFalse();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        (await dbContext.Set<OutboxMessage>().AnyAsync(TestContext.Current.CancellationToken))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_ExpiredCode_DoesNotWriteOutboxRow()
    {
        using var client = factory.CreateClient();
        var (email, _) = await RegisterAsync(client);
        var code = ExtractCode(factory.EmailSender.Sent.Single());

        factory.TimeProvider.Advance(TimeSpan.FromMinutes(21));

        var response = await client.PostAsJsonAsync(
            "/api/identity/auth/confirm-email",
            new ConfirmEmailRequest(email, code),
            JsonOptions,
            TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.Should().BeFalse();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        (await dbContext.Set<OutboxMessage>().AnyAsync(TestContext.Current.CancellationToken))
            .Should().BeFalse();
    }

    private static string ExtractCode(EmailMessage message) => SixDigitCode().Match(message.HtmlBody).Value;

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
