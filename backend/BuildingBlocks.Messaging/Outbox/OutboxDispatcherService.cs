using BuildingBlocks.Messaging.Tracing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;

namespace BuildingBlocks.Messaging.Outbox;

// Producer-side relay: polls outbox_messages for unsent rows and publishes
// each through IEventBus, at-least-once (ADR-0010). Rows are claimed with
// FOR UPDATE SKIP LOCKED so that if this service is ever run as multiple
// replicas, each claims a disjoint set instead of racing to publish the same
// row twice - the same reason the in-process rate limiter from slice 1
// isn't authoritative once you're running more than one instance.
public sealed partial class OutboxDispatcherService<TDbContext>(
    IServiceScopeFactory scopeFactory,
    IEventBus eventBus,
    IOptions<OutboxDispatcherOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcherService<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A poll cycle can fail before it ever reaches a message (the
                // database itself unreachable, say). That is not a reason to
                // let ASP.NET Core's default BackgroundServiceExceptionBehavior
                // take the whole host down - log it and try again next poll.
                LogDispatchCycleFailed(ex);
            }

            await Task.Delay(pollInterval, timeProvider, stoppingToken);
        }
    }

    private async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        // Held for the whole claim-publish-mark cycle: releasing the lock
        // before publishing would let a concurrent poll grab the same row.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var batch = await dbContext.Set<OutboxMessage>()
            .FromSqlInterpolated($"""
                SELECT * FROM outbox_messages
                WHERE processed_at IS NULL AND attempts < {settings.MaxAttempts}
                ORDER BY occurred_at
                LIMIT {settings.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        foreach (var message in batch)
        {
            await PublishOneAsync(message, settings, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task PublishOneAsync(OutboxMessage message, OutboxDispatcherOptions settings, CancellationToken cancellationToken)
    {
        var pipeline = BuildRetryPipeline(message, settings.MaxAttempts);

        // One Producer activity per outbox row, started once outside the retry loop: a retried
        // publish is the same logical send happening again, not a second one, so every attempt
        // carries the same traceparent headers rather than minting a new trace id per retry.
        using var activity = MessagingTracePropagation.StartProducerActivity(message.TraceParent, out var headers);

        try
        {
            await pipeline.ExecuteAsync(
                ct => new ValueTask(eventBus.PublishAsync(
                    new EventEnvelope(message.Type, message.Version, message.Payload), headers, ct)),
                cancellationToken);

            message.ProcessedAt = timeProvider.GetUtcNow();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The last attempt in an exhausted retry sequence never triggers
            // OnRetry (there is no further retry to announce), so it is
            // accounted for here instead.
            message.Attempts++;

            if (message.Attempts >= settings.MaxAttempts)
            {
                LogMessageParked(ex, message.Id, message.Type, message.Attempts);
            }
        }
    }

    private ResiliencePipeline BuildRetryPipeline(OutboxMessage message, int maxAttempts) =>
        new ResiliencePipelineBuilder { TimeProvider = timeProvider }
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = Math.Max(1, maxAttempts - 1),
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(200),
                OnRetry = _ =>
                {
                    message.Attempts++;
                    return default;
                },
            })
            .Build();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Parking outbox message {MessageId} of type {MessageType} after {Attempts} failed publish attempts")]
    private partial void LogMessageParked(Exception exception, Guid messageId, string messageType, int attempts);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Outbox dispatcher poll cycle failed, retrying on the next poll")]
    private partial void LogDispatchCycleFailed(Exception exception);
}
