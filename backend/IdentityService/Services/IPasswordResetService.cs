namespace IdentityService.Services;

public interface IPasswordResetService
{
    Task RequestResetAsync(string email, CancellationToken cancellationToken = default);

    Task<string> CompleteResetAsync(string rawToken, string newPassword, CancellationToken cancellationToken = default);
}
