using AwesomeAssertions;
using IdentityService.Domain;
using IdentityService.Persistence;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests.Integration;

[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed class ExternalLoginSchemaTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SaveChanges_DuplicateProviderAndProviderUserId_ViolatesTheUniqueIndex()
    {
        var firstUserId = await SeedUserAsync();
        var secondUserId = await SeedUserAsync();

        await InsertLoginAsync(firstUserId, provider: "google", providerUserId: "shared-subject");

        var act = async () => await InsertLoginAsync(secondUserId, provider: "google", providerUserId: "shared-subject");

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task SaveChanges_SameProviderUserIdOnDifferentProvider_Succeeds()
    {
        var userId = await SeedUserAsync();
        await InsertLoginAsync(userId, provider: "google", providerUserId: "shared-subject");

        var act = async () => await InsertLoginAsync(userId, provider: "microsoft", providerUserId: "shared-subject");

        await act.Should().NotThrowAsync();
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

    private async Task InsertLoginAsync(Guid userId, string provider, string providerUserId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        dbContext.ExternalLogins.Add(new ExternalLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Provider = provider,
            ProviderUserId = providerUserId,
            LinkedAt = factory.TimeProvider.GetUtcNow(),
        });

        await dbContext.SaveChangesAsync();
    }
}
