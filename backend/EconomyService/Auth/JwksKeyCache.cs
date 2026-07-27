using EconomyService.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EconomyService.Auth;

public sealed partial class JwksKeyCache : IJwksKeyCache
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<JwtOptions> _options;
    private readonly JwksKeySnapshot _snapshot;
    private readonly ILogger<JwksKeyCache> _logger;

    public JwksKeyCache(HttpClient httpClient, IOptions<JwtOptions> options, JwksKeySnapshot snapshot, ILogger<JwksKeyCache> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _snapshot = snapshot;
        _logger = logger;
    }

    public IReadOnlyList<SecurityKey> CurrentKeys => _snapshot.Current;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await _httpClient.GetStringAsync(_options.Value.JwksUri, cancellationToken);
            var keys = new JsonWebKeySet(json).GetSigningKeys().ToList();

            _snapshot.Replace(keys);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A transient network blip during a scheduled refresh shouldn't suddenly make
            // every token invalid when the previous snapshot is still (probably) correct.
            // The one case this doesn't cover is the very first refresh ever attempted --
            // with nothing cached yet, there is no last-known-good to fall back on, so this
            // rethrows and lets the caller (the blocking startup refresh in Program.cs) fail
            // the same way ValidateOnStart already does for configuration.
            if (_snapshot.Current.Count == 0)
            {
                throw;
            }

            LogRefreshFailed(ex, _options.Value.JwksUri);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Failed to refresh JWKS signing keys from {JwksUri}; continuing with the last known good snapshot")]
    private partial void LogRefreshFailed(Exception exception, string jwksUri);
}
