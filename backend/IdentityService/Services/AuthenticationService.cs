using IdentityService.Auth;
using IdentityService.Domain;
using IdentityService.Domain.Enums;
using IdentityService.Exceptions;
using IdentityService.Persistence;
using IdentityService.Services.Email;
using IdentityService.Services.Email.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IdentityService.Services;

public sealed partial class AuthenticationService(
    IdentityDbContext dbContext,
    IPasswordHasher passwordHasher,
    IEmailVerificationService emailVerificationService,
    IRefreshTokenService refreshTokenService,
    ITokenService tokenService,
    IPermissionResolver permissionResolver,
    IEmailSender emailSender,
    IEmailTemplateRenderer templateRenderer,
    TimeProvider timeProvider,
    ILogger<AuthenticationService> logger) : IAuthenticationService
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);


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

            if (!hasRoleInGame)
            {
                dbContext.UserGameRoles.Add(NewPlayerRole(user.Id, game.Id, now));
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            else if (user.EmailConfirmed)
            {
                await SendDuplicateRegistrationNoticeAsync(user.Id, user.Email, game.Name, cancellationToken);
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
        string audience,
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
            if (gameId is not null)
            {
                throw new NoAccessToGameException();
            }

            var accountIssued = await refreshTokenService.IssueFamilyAsync(
                user.Id, null, TokenScope.Account, ip, userAgent, cancellationToken);
            var accountAccessToken = tokenService.IssueAccessToken(
                user, null, null, accountIssued.Family.Id, TokenScope.Account, AccountPermissions.All, audience);

            return new LoginResult(accountAccessToken, accountIssued.RawToken);
        }

        var scope = gameId is null ? TokenScope.Platform : TokenScope.Game;
        var permissions = await permissionResolver.ResolveAsync(role.Role, gameId, cancellationToken);

        var issued = await refreshTokenService.IssueFamilyAsync(user.Id, gameId, scope, ip, userAgent, cancellationToken);
        var accessToken = tokenService.IssueAccessToken(user, gameId, role.Role, issued.Family.Id, scope, permissions, audience);

        return new LoginResult(accessToken, issued.RawToken);
    }

    public async Task<LoginResult> SelectGameAsync(
        Guid userId,
        Guid gameId,
        string? ip,
        string? userAgent,
        string audience,
        CancellationToken cancellationToken = default)
    {
        var game = await dbContext.Games
            .SingleOrDefaultAsync(g => g.Id == gameId && g.IsActive, cancellationToken);

        if (game is null)
        {
            throw new GameNotFoundException();
        }

        var user = await dbContext.Users.SingleAsync(u => u.Id == userId, cancellationToken);

        var role = await dbContext.UserGameRoles
            .SingleOrDefaultAsync(r => r.UserId == userId && r.GameId == gameId, cancellationToken);

        var now = timeProvider.GetUtcNow();

        if (role is null)
        {
            role = NewPlayerRole(userId, gameId, now);
            dbContext.UserGameRoles.Add(role);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var permissions = await permissionResolver.ResolveAsync(role.Role, gameId, cancellationToken);
        var issued = await refreshTokenService.IssueFamilyAsync(userId, gameId, TokenScope.Game, ip, userAgent, cancellationToken);
        var accessToken = tokenService.IssueAccessToken(user, gameId, role.Role, issued.Family.Id, TokenScope.Game, permissions, audience);

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

    private async Task SendDuplicateRegistrationNoticeAsync(
        Guid userId, string email, string gameName, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(SendTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var htmlBody = templateRenderer.RenderDuplicateRegistrationNotice(gameName);
            var textBody =
                $"Someone attempted to register an account for {gameName} using this email address. " +
                "If this was not you, you can safely ignore this message -- no changes were made to your account.";

            await emailSender.SendAsync(
                new EmailMessage(email, "Registration attempt on your email address", htmlBody, textBody),
                linkedCts.Token);
        }
        catch (Exception exception)
        {
            LogDuplicateRegistrationNoticeSendFailed(exception, userId);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send duplicate registration notice email for user {UserId}")]
    private partial void LogDuplicateRegistrationNoticeSendFailed(Exception exception, Guid userId);
}
