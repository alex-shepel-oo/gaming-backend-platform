namespace IdentityService.Services;

public sealed record RegistrationResult(Guid UserId, string Email, bool VerificationRequired, DateTimeOffset? CodeExpiresAt);

public sealed record LoginResult(string AccessToken, string RefreshToken);

public interface IAuthenticationService
{
    Task<RegistrationResult> RegisterAsync(
        string gameSlug,
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken = default);

    Task<LoginResult> LoginAsync(
        string? gameSlug,
        string email,
        string password,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken = default);

    Task<LoginResult> SelectGameAsync(
        Guid userId, Guid gameId, string? ip, string? userAgent, CancellationToken cancellationToken = default);
}
