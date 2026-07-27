using Microsoft.IdentityModel.Tokens;

namespace EconomyService.Auth;

public interface IJwksKeyCache
{
    IReadOnlyList<SecurityKey> CurrentKeys { get; }

    Task RefreshAsync(CancellationToken cancellationToken);
}
