using AwesomeAssertions;
using NUnit.Framework;

namespace Platform.Worker.Tests;

[TestFixture]
public sealed class WorkerHostSmokeTests
{
    [Test]
    public async Task Host_starts_and_stops_without_throwing()
    {
        using var host = WorkerHostBuilder.Create([]).Build();

        var act = async () =>
        {
            await host.StartAsync(TestContext.CurrentContext.CancellationToken);
            await host.StopAsync(TestContext.CurrentContext.CancellationToken);
        };

        await act.Should().NotThrowAsync();
    }
}
