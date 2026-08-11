using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.Auth;

public interface IJwksKeyCache
{
    IReadOnlyList<SecurityKey> CurrentKeys { get; }

    Task RefreshAsync(CancellationToken cancellationToken);
}
