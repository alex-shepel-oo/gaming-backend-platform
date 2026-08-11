# Observability overview

## Stack

`BuildingBlocks.Telemetry`'s `AddPlatformTelemetry` wires OpenTelemetry (ASP.NET Core, HttpClient, EF
Core instrumentation) identically across every backend service, exporting traces and metrics via OTLP
to a single `otel-collector`, which fans out to Tempo (traces) and a Prometheus-scrapeable endpoint
(metrics). Serilog keeps its correlation-ID-enriched Console sink and gains a Loki sink alongside it.
The collector, Tempo, Prometheus, Loki, and Grafana all run in their own `observability` namespace,
separate from the app namespace, so the two lifecycles don't couple — see
[Backend deployment topology](../architecture/backend.md) for the `ExternalName` alias that lets both
frontend apps still reach `otel-collector` by its bare hostname across that namespace split. See
[ADR 0019](../adr/0019-opentelemetry-observability.md).

Retention on Loki/Tempo/Prometheus is configurable (`LOKI_RETENTION_PERIOD`/`TEMPO_RETENTION_PERIOD`/
`PROMETHEUS_RETENTION_PERIOD`, all defaulting to roughly six months), not left unbounded.

## Trace propagation across the async outbox path

A trace doesn't stop at the edge of a synchronous request. `OutboxMessage.trace_parent` persists the
writing request's trace context across the outbox dispatcher's poll cycle, re-parented and propagated
onward through standard W3C headers on the AMQP message — so a trace that starts at, say, a
registration request can continue through `IdentityService → RabbitMQ → EconomyService`'s welcome
grant consumer as one connected trace, not two disconnected ones that happen to be related. The same
mechanism carries a trace from `EconomyService`'s `BalanceChanged` publish through to
`NotificationService`'s SignalR push. See [Messaging](../messaging.md) and
`BuildingBlocks.Messaging`'s `MessagingTracePropagation` in
[BuildingBlocks](../services/building-blocks.md).

## Frontend tracing

Both Angular apps export tracing spans via Grafana Faro straight to the same `otel-collector`,
proxied same-origin through each app's own Nginx (`/otlp/`) rather than the browser talking to
`otel-collector` directly. A trace now runs from an actual browser click through the gateway into
whichever backend service (and, where relevant, the outbox/RabbitMQ/SignalR path above) handled it.
Faro's error/session/Web-Vitals capture is deliberately not wired up — that data needs a dedicated
Faro receiver (e.g. Grafana Alloy) this stack doesn't run, and standing one up wasn't a decision to
make as a side effect of instrumenting tracing. The telemetry setup itself loads behind a lazy dynamic
import so neither app blocks on it before becoming interactive. See
[ADR 0020](../adr/0020-frontend-tracing-with-faro.md).

## Identity tagging

Every authenticated request and messaging span carrying a known user id is tagged `enduser.id`
(`BuildingBlocks.Telemetry`'s `OtelConventions` centralizes the attribute name so every service uses
the same key), so one player's activity is filterable across every service's traces and logs at once
— demonstrated live on the demo Grafana instance's own dashboard.

## Walking one real trace

Registering a new player is the clearest real example crossing both a synchronous request and the
async outbox path: the browser's `POST /auth/register` request is the trace root; IdentityService
persists the user and writes `UserEmailConfirmedEvent` to its own outbox once email confirmation
completes; the outbox dispatcher picks up the row on its next poll and publishes to RabbitMQ, carrying
`trace_parent` forward; EconomyService's `UserEmailConfirmedConsumer` continues the same trace,
grants the starting balance, and publishes `BalanceChangedEvent`; NotificationService relays that as a
SignalR push, still tagged with the same `enduser.id` as the very first request. One trace, four
services, one HTTP request and two asynchronous hops — verified on the live demo's Grafana instance,
not a theoretical description.

## Health checks

`/health` (liveness) and `/health/ready` (readiness, checks real dependencies like RabbitMQ/database
connectivity where relevant) exist on IdentityService, EconomyService, NotificationService, and
ApiGateway. **EmailService and Platform.Worker have neither** — both are plain generic hosts with no
HTTP surface at all, so there's nowhere to expose one without adding a web listener neither service
otherwise needs.

## Dashboards

The main service-overview dashboard includes a query that surfaces real cross-service traces crossing
RabbitMQ, not just synchronous request timing. A separate dashboard tracks live node/pod resource
usage. Both are provisioned from `infra/grafana/dashboards/*.json`, not clicked together manually in
the UI.

## Related documentation

- [ADR 0019: OpenTelemetry observability](../adr/0019-opentelemetry-observability.md)
- [ADR 0020: Frontend tracing with Faro](../adr/0020-frontend-tracing-with-faro.md)
- [Messaging](../messaging.md)
- [BuildingBlocks](../services/building-blocks.md)
- [Backend deployment topology](../architecture/backend.md)
