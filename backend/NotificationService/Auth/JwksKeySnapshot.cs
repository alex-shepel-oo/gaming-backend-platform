using Microsoft.IdentityModel.Tokens;

namespace NotificationService.Auth;

// Holds the actual key snapshot, kept singleton and separate from JwksKeyCache itself:
// AddHttpClient<TClient, TImplementation> always registers the typed client as transient
// (a fresh instance, with its own pooled HttpClient, per resolution), so the background
// refresher and the JwtBearerOptions resolver would otherwise end up observing two unrelated
// JwksKeyCache instances instead of one shared, continuously-refreshed snapshot. Routing the
// mutable state through this singleton keeps every JwksKeyCache instance reading and writing
// the same keys regardless of how many transient instances get created.
public sealed class JwksKeySnapshot
{
    private IReadOnlyList<SecurityKey> _keys = [];

    public IReadOnlyList<SecurityKey> Current => Volatile.Read(ref _keys);

    public void Replace(IReadOnlyList<SecurityKey> keys) => Interlocked.Exchange(ref _keys, keys);
}
