using Microsoft.IdentityModel.Tokens;

namespace NotificationService.Auth;

public interface IJwksKeyCache
{
    IReadOnlyList<SecurityKey> CurrentKeys { get; }

    Task RefreshAsync(CancellationToken cancellationToken);
}
