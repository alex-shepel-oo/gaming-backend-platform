using Microsoft.IdentityModel.Tokens;

namespace ApiGateway.Auth;

public interface IJwksKeyCache
{
    IReadOnlyList<SecurityKey> CurrentKeys { get; }

    Task RefreshAsync(CancellationToken cancellationToken);
}
