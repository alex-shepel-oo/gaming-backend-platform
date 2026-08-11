namespace IdentityService.Services;

public interface IPasswordResetService
{
    Task RequestResetAsync(string email, CancellationToken cancellationToken = default);

    Task<string> CompleteResetAsync(string rawToken, string newPassword, CancellationToken cancellationToken = default);

    // Read-only check the reset-password page calls on load so it can show "this link is
    // invalid or expired" immediately, before the player has typed a new password at all --
    // same validity rule as CompleteResetAsync, just without consuming the token or touching
    // the user's password.
    Task ValidateTokenAsync(string rawToken, CancellationToken cancellationToken = default);
}
