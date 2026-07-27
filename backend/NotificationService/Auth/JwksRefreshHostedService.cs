namespace NotificationService.Auth;

// Re-polls Identity's JWKS on a schedule of the same order of magnitude as the access
// token lifetime, so a key rotated on Identity's side is picked up here well before any
// token signed under it would otherwise expire. The first, already-warm snapshot comes
// from the blocking refresh Program.cs runs before the app starts accepting requests --
// this loop only keeps that snapshot from ever going stale.
public sealed class JwksRefreshHostedService(IJwksKeyCache jwksKeyCache) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await jwksKeyCache.RefreshAsync(stoppingToken);
        }
    }
}
