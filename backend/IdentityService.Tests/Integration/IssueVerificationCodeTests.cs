using AwesomeAssertions;
using IdentityService.Domain;
using IdentityService.Persistence;
using IdentityService.Services;
using IdentityService.Tests.Integration.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IdentityService.Tests.Integration;

[Collection(nameof(IdentityApiCollectionDefinition))]
public sealed class IssueVerificationCodeTests(IdentityApiFactory factory) : IClassFixture<IdentityApiFactory>, IAsyncLifetime
{
    public ValueTask InitializeAsync() => new(factory.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task IssueCode_WhenActiveCodeExists_SupersedesOldCodeAndKeepsExactlyOneActive()
    {
        var userId = await SeedUserAsync();

        var first = await IssueCodeAsync(userId);
        var second = await IssueCodeAsync(userId);

        var oldCode = await FindCodeAsync(first.Code.Id);
        oldCode.ConsumedAt.Should().NotBeNull();
        second.Code.ConsumedAt.Should().BeNull();

        var activeCodeCount = await CountActiveCodesAsync(userId);
        activeCodeCount.Should().Be(1);
    }

    [Fact]
    public async Task IssueCode_SecondActiveRowBypassingTheService_ViolatesTheUniqueIndex()
    {
        var userId = await SeedUserAsync();
        await IssueCodeAsync(userId);

        var act = async () => await InsertActiveCodeDirectlyAsync(userId);

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
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return user.Id;
    }

    private async Task<EmailVerificationIssueResult> IssueCodeAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IEmailVerificationService>();

        return await service.IssueCodeAsync(userId, gameId: null, email: "player@example.com");
    }

    private async Task<EmailVerificationCode> FindCodeAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.EmailVerificationCodes.SingleAsync(c => c.Id == id);
    }

    private async Task<int> CountActiveCodesAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        return await dbContext.EmailVerificationCodes
            .CountAsync(c => c.UserId == userId && c.ConsumedAt == null);
    }

    private async Task InsertActiveCodeDirectlyAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = factory.TimeProvider.GetUtcNow();

        dbContext.EmailVerificationCodes.Add(new EmailVerificationCode
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            CodeHash = "irrelevant",
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(20),
            AttemptCount = 0,
            SentToEmail = "player@example.com",
        });

        await dbContext.SaveChangesAsync();
    }
}
