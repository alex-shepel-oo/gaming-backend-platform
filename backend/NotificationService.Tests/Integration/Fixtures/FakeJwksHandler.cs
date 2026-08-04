using System.Net;
using System.Text;

namespace NotificationService.Tests.Integration.Fixtures;

// Stands in for Identity's real /.well-known/jwks.json during tests: JwksKeyCache is wired
// against this instead of a live HTTP endpoint, so tests can both assert on how many times the
// cache actually fetches (once per refresh, not once per validated token) and force a fetch
// failure without needing a real network to break.
public sealed class FakeJwksHandler(string jwksJson) : HttpMessageHandler
{
    private int _requestCount;

    public int RequestCount => Volatile.Read(ref _requestCount);

    public bool ShouldFail { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCount);

        if (ShouldFail)
        {
            throw new HttpRequestException("Simulated JWKS endpoint failure.");
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jwksJson, Encoding.UTF8, "application/json"),
        };

        return Task.FromResult(response);
    }
}
