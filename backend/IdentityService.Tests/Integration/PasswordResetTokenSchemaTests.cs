using AwesomeAssertions;
using IdentityService.Domain;
using IdentityService.Persistence;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests.Integration;

[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed class PasswordResetTokenSchemaTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SaveChanges_SecondActiveTokenForSameUser_ViolatesTheUniqueIndex()
    {
        var userId = await SeedUserAsync();
        await InsertActiveTokenAsync(userId);

        var act = async () => await InsertActiveTokenAsync(userId);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SaveChanges_ConsumedThenNewActiveTokenForSameUser_Succeeds()
    {
        var userId = await SeedUserAsync();
        var firstId = await InsertActiveTokenAsync(userId);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var first = await dbContext.PasswordResetTokens.SingleAsync(t => t.Id == firstId, TestContext.Current.CancellationToken);
            first.ConsumedAt = factory.TimeProvider.GetUtcNow();
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var act = async () => await InsertActiveTokenAsync(userId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SaveChanges_DuplicateTokenHash_ViolatesTheUniqueIndex()
    {
        var firstUserId = await SeedUserAsync();
        var secondUserId = await SeedUserAsync();
        var sharedHash = "same-hash"u8.ToArray();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = factory.TimeProvider.GetUtcNow();

        dbContext.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.CreateVersion7(),
            UserId = firstUserId,
            TokenHash = sharedHash,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(30),
        });
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        dbContext.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.CreateVersion7(),
            UserId = secondUserId,
            TokenHash = sharedHash,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(30),
        });

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private async Task<Guid> SeedUserAsync()
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
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return user.Id;
    }

    private async Task<Guid> InsertActiveTokenAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = factory.TimeProvider.GetUtcNow();

        var token = new PasswordResetToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = Guid.NewGuid().ToByteArray(),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(30),
        };

        dbContext.PasswordResetTokens.Add(token);
        await dbContext.SaveChangesAsync();

        return token.Id;
    }
}
