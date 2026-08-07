using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EconomyService.Services;

// Drains the channel fed by POST /conversions and runs the saga for each id
// on a hosted background service, not Task.Run - a fire-and-forget task is
// lost on process restart and isn't observable the way a hosted service is.
// ConversionSaga.ExecuteAsync stays directly callable and directly
// testable without HTTP; this is just what feeds it after the request that
// queued the work has already returned 202.
public sealed partial class ConversionSagaRunner(
    ConversionSagaChannel sagaChannel,
    IServiceScopeFactory scopeFactory,
    ILogger<ConversionSagaRunner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var conversionId in sagaChannel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var saga = scope.ServiceProvider.GetRequiredService<IConversionSaga>();
                await saga.ExecuteAsync(conversionId, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The saga already turns its own known failures (a bad
                // credit step) into a Failed status; this only catches the
                // unexpected - a dropped database connection, say - so one
                // bad item doesn't take the whole runner down.
                LogSagaExecutionFailed(ex, conversionId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Conversion saga execution failed for conversion {ConversionId}")]
    private partial void LogSagaExecutionFailed(Exception exception, Guid conversionId);
}
