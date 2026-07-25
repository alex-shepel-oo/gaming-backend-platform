using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using IdentityService.Contracts.Requests;
using IdentityService.Domain;
using IdentityService.Persistence;
using IdentityService.Services;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests.Integration;

[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed class RequestPasswordResetTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task RequestReset_ExistingEmail_Returns202AndSendsLinkWithValidToken()
    {
        using var client = factory.CreateClient();
        var user = await SeedUserAsync();

        var response = await RequestResetAsync(client, user.Email);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.EmailSender.Sent.Should().HaveCount(1);

        var sent = factory.EmailSender.Sent.Single();
        sent.HtmlBody.Should().Contain("http://localhost:8080/reset-password?token=");

        var rawToken = ExtractToken(sent.HtmlBody);
        var tokenGenerator = factory.Services.GetRequiredService<IRefreshTokenGenerator>();

        var storedToken = await FindActiveTokenAsync(user.Id);
        storedToken.Should().NotBeNull();
        storedToken!.TokenHash.Should().BeEquivalentTo(tokenGenerator.Hash(rawToken));
    }

    [Fact]
    public async Task RequestReset_UnknownEmail_Returns202AndSendsNothing()
    {
        using var client = factory.CreateClient();

        var response = await RequestResetAsync(client, $"{Guid.NewGuid():N}@example.com");

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.EmailSender.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestReset_KnownAndUnknownEmail_ReturnIdenticalResponseShape()
    {
        using var client = factory.CreateClient();
        var user = await SeedUserAsync();

        var known = await RequestResetAsync(client, user.Email);
        var unknown = await RequestResetAsync(client, $"{Guid.NewGuid():N}@example.com");

        var knownBody = await known.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var unknownBody = await unknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        known.StatusCode.Should().Be(unknown.StatusCode);
        knownBody.Should().Be(unknownBody);
    }

    [Fact]
    public async Task RequestReset_WithinCooldown_DoesNotSendSecondEmailOrCreateSecondToken()
    {
        using var client = factory.CreateClient();
        var user = await SeedUserAsync();

        var first = await RequestResetAsync(client, user.Email);
        var firstToken = await FindActiveTokenAsync(user.Id);

        var second = await RequestResetAsync(client, user.Email);

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        second.StatusCode.Should().Be(HttpStatusCode.Accepted);
        factory.EmailSender.Sent.Should().HaveCount(1);

        var allTokens = await FindAllTokensAsync(user.Id);
        allTokens.Should().ContainSingle(t => t.ConsumedAt == null && t.Id == firstToken!.Id);
    }

    [Fact]
    public async Task RequestReset_AfterCooldown_SupersedesPreviousToken()
    {
        using var client = factory.CreateClient();
        var user = await SeedUserAsync();

        await RequestResetAsync(client, user.Email);
        var firstToken = await FindActiveTokenAsync(user.Id);

        factory.TimeProvider.Advance(TimeSpan.FromSeconds(61));
        await RequestResetAsync(client, user.Email);

        var reloadedFirst = await FindTokenByIdAsync(firstToken!.Id);
        reloadedFirst!.ConsumedAt.Should().NotBeNull();

        var activeToken = await FindActiveTokenAsync(user.Id);
        activeToken.Should().NotBeNull();
        activeToken!.Id.Should().NotBe(firstToken.Id);

        factory.EmailSender.Sent.Should().HaveCount(2);
    }

    private static Task<HttpResponseMessage> RequestResetAsync(HttpClient client, string email) =>
        client.PostAsJsonAsync(
            "/api/identity/auth/request-password-reset",
            new RequestPasswordResetRequest(email),
            JsonOptions,
            TestContext.Current.CancellationToken);

    private static string ExtractToken(string htmlBody)
    {
        const string marker = "token=";
        var start = htmlBody.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = htmlBody.IndexOf('"', start);

        return htmlBody[start..end];
    }

    private async Task<User> SeedUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = factory.TimeProvider.GetUtcNow();

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = $"{Guid.NewGuid():N}@example.com",
            DisplayName = "Test Player",
            PasswordHash = "irrelevant",
            IsActive = true,
            EmailConfirmed = true,
            EmailConfirmedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return user;
    }

    private async Task<PasswordResetToken?> FindActiveTokenAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.PasswordResetTokens
            .SingleOrDefaultAsync(t => t.UserId == userId && t.ConsumedAt == null);
    }

    private async Task<PasswordResetToken?> FindTokenByIdAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.PasswordResetTokens.SingleOrDefaultAsync(t => t.Id == id);
    }

    private async Task<List<PasswordResetToken>> FindAllTokensAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.PasswordResetTokens.Where(t => t.UserId == userId).ToListAsync();
    }
}
