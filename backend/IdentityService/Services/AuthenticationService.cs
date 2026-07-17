using IdentityService.Domain;
using IdentityService.Domain.Enums;
using IdentityService.Exceptions;
using IdentityService.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Services;

public sealed class AuthenticationService(
    IdentityDbContext dbContext,
    IPasswordHasher passwordHasher,
    IEmailVerificationService emailVerificationService,
    IRefreshTokenService refreshTokenService,
    ITokenService tokenService,
    TimeProvider timeProvider) : IAuthenticationService
{
    public async Task<RegistrationResult> RegisterAsync(
        string gameSlug,
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken = default)
    {
        var game = await dbContext.Games
            .SingleOrDefaultAsync(g => g.Slug == gameSlug && g.IsActive, cancellationToken);

        if (game is null)
        {
            throw new GameNotFoundException();
        }

        var now = timeProvider.GetUtcNow();

        // ToLower() here is translated to SQL lower(), matching the functional unique
        // index on lower(email) from the initial migration -- it never runs as a CLR
        // string method, so the culture/StringComparison analyzers do not apply.
#pragma warning disable CA1304, CA1311, CA1862
        var user = await dbContext.Users
            .SingleOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862

        if (user is null)
        {
            user = new User
            {
                Id = Guid.CreateVersion7(),
                Email = email,
                DisplayName = displayName,
                PasswordHash = passwordHasher.Hash(password),
                IsActive = true,
                EmailConfirmed = false,
                CreatedAt = now,
                UpdatedAt = now,
            };

            dbContext.Users.Add(user);
            dbContext.UserGameRoles.Add(NewPlayerRole(user.Id, game.Id, now));

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            var hasRoleInGame = await dbContext.UserGameRoles
                .AnyAsync(r => r.UserId == user.Id && r.GameId == game.Id, cancellationToken);

            if (user.EmailConfirmed && hasRoleInGame)
            {
                throw new EmailAlreadyExistsException();
            }

            if (!hasRoleInGame)
            {
                dbContext.UserGameRoles.Add(NewPlayerRole(user.Id, game.Id, now));
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            if (user.EmailConfirmed)
            {
                return new RegistrationResult(user.Id, user.Email, VerificationRequired: false, CodeExpiresAt: null);
            }
        }

        var issuedCode = await emailVerificationService.IssueAndSendCodeAsync(
            user.Id, game.Id, user.Email, game.Name, cancellationToken);

        return new RegistrationResult(user.Id, user.Email, VerificationRequired: true, issuedCode.ExpiresAt);
    }

    public async Task<LoginResult> LoginAsync(
        string? gameSlug,
        string email,
        string password,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        Guid? gameId = null;

        if (gameSlug is not null)
        {
            var game = await dbContext.Games
                .SingleOrDefaultAsync(g => g.Slug == gameSlug && g.IsActive, cancellationToken);

            if (game is null)
            {
                throw new GameNotFoundException();
            }

            gameId = game.Id;
        }

        // ToLower() here is translated to SQL lower(), matching the functional unique
        // index on lower(email) -- it never runs as a CLR string method.
#pragma warning disable CA1304, CA1311, CA1862
        var user = await dbContext.Users
            .SingleOrDefaultAsync(u => u.Email.ToLower() == email.ToLower(), cancellationToken);
#pragma warning restore CA1304, CA1311, CA1862

        if (user is null || !passwordHasher.Verify(password, user.PasswordHash))
        {
            throw new InvalidCredentialsException();
        }

        if (!user.IsActive)
        {
            throw new AccountDisabledException();
        }

        if (!user.EmailConfirmed)
        {
            throw new EmailNotConfirmedException();
        }

        var role = await dbContext.UserGameRoles
            .SingleOrDefaultAsync(r => r.UserId == user.Id && r.GameId == gameId, cancellationToken);

        if (role is null)
        {
            throw new NoAccessToGameException();
        }

        var issued = await refreshTokenService.IssueFamilyAsync(user.Id, gameId, ip, userAgent, cancellationToken);
        var accessToken = tokenService.IssueAccessToken(user, gameId, role.Role, issued.Family.Id);

        return new LoginResult(accessToken, issued.RawToken);
    }

    private static UserGameRole NewPlayerRole(Guid userId, Guid gameId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        GameId = gameId,
        Role = PlatformRole.Player,
        GrantedAt = now,
    };
}
