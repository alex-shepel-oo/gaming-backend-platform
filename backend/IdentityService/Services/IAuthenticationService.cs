namespace IdentityService.Services;

public sealed record RegistrationResult(Guid UserId, string Email, bool VerificationRequired, DateTimeOffset? CodeExpiresAt);

public interface IAuthenticationService
{
    Task<RegistrationResult> RegisterAsync(
        string gameSlug,
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken = default);
}
