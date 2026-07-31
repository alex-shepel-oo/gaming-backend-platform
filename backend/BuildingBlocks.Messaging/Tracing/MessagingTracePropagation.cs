using System.Diagnostics;
using System.Text;
using OpenTelemetry.Context.Propagation;

namespace BuildingBlocks.Messaging.Tracing;

// Shared by both consumer shapes in this codebase (InboxConsumerBase<TDbContext>'s DB-backed dedup
// loop and NotificationService's hand-rolled BalanceChangedConsumer, which has no DbContext to be
// generic over) so the extract-and-start-activity step exists exactly once, and by the outbox
// dispatcher on the producer side - all three talk to the AMQP header table the same way, via
// OpenTelemetry's own W3C propagator rather than a hand-rolled header format.
public static class MessagingTracePropagation
{
    private const string ProducerActivityName = "outbox publish";

    /// <summary>
    /// Producer side: starts an <see cref="ActivityKind.Producer"/> activity parented to the outbox
    /// row's persisted <c>TraceParent</c> - not <see cref="Activity.Current"/>, which by dispatch
    /// time belongs to an unrelated poll cycle, not the request that originally wrote the row - and
    /// returns the W3C headers to attach to the outgoing AMQP message. A null or unparsable
    /// <paramref name="traceParent"/> (a row written before this column existed, or by a caller that
    /// never captured one) falls back to a fresh root activity instead of throwing.
    /// </summary>
    public static Activity? StartProducerActivity(string? traceParent, out IReadOnlyDictionary<string, string> headers)
    {
        var activity = traceParent is not null && ActivityContext.TryParse(traceParent, traceState: null, out var parentContext)
            ? MessagingActivitySource.Instance.StartActivity(ProducerActivityName, ActivityKind.Producer, parentContext)
            : MessagingActivitySource.Instance.StartActivity(ProducerActivityName, ActivityKind.Producer);

        var injectedHeaders = new Dictionary<string, string>();
        Propagators.DefaultTextMapPropagator.Inject(
            new PropagationContext(activity?.Context ?? default, default),
            injectedHeaders,
            static (carrier, key, value) => carrier[key] = value);

        headers = injectedHeaders;
        return activity;
    }

    /// <summary>
    /// Consumer side: extracts whatever W3C headers rode along on the AMQP delivery and starts an
    /// <see cref="ActivityKind.Consumer"/> activity parented to that context. Headers arrive from
    /// RabbitMQ.Client as <c>byte[]</c> (the AMQP wire decodes header tables that way), not the
    /// <c>string</c> a caller sets them as when publishing in-process - both are handled. A delivery
    /// with no headers at all (published before this session, or by a producer that never set them)
    /// extracts to a default context, which falls back to a fresh root activity rather than an error.
    /// </summary>
    public static Activity? StartConsumerActivity(string activityName, IDictionary<string, object?>? amqpHeaders)
    {
        var propagationContext = Propagators.DefaultTextMapPropagator.Extract(
            default,
            amqpHeaders,
            static (carrier, key) => ExtractHeaderValues(carrier, key));

        return propagationContext.ActivityContext != default
            ? MessagingActivitySource.Instance.StartActivity(activityName, ActivityKind.Consumer, propagationContext.ActivityContext)
            : MessagingActivitySource.Instance.StartActivity(activityName, ActivityKind.Consumer);
    }

    private static IEnumerable<string> ExtractHeaderValues(IDictionary<string, object?>? carrier, string key)
    {
        if (carrier is null || !carrier.TryGetValue(key, out var value) || value is null)
        {
            return [];
        }

        return value switch
        {
            byte[] bytes => [Encoding.UTF8.GetString(bytes)],
            string text => [text],
            _ => [],
        };
    }
}
