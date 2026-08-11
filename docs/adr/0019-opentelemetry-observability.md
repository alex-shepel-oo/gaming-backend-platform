# ADR-0019: OpenTelemetry observability with a collector hub and Grafana-native backends

- **Status:** Accepted
- **Date:** 2026-07-31

## Context

The master plan named correlation-id propagation and structured logging as MVP observability, with
"full OpenTelemetry distributed tracing" explicitly called out as the next, Extended-phase step —
not scope creep, the already-intended follow-on. By this point every service had `CorrelationIdMiddleware`
and Serilog writing structured JSON to Console, but nothing shipped anywhere: no metrics, no traces,
no log aggregation, and no way to see a request's path once it left the service that first handled it.

Two problems compound this. First, the platform's async messaging (outbox → RabbitMQ → inbox →
SignalR) genuinely disconnects from the HTTP request that triggered it: the outbox dispatcher is a
background poll loop running seconds or minutes after the request that wrote the row has already
finished, so whatever tracing exists has no ambient context to attach to at dispatch time. Second,
a single request or log line carries no notion of *which player* it belongs to — diagnosing "what did
this user experience" meant grep-ing timestamps across services by hand.

## Decision

**Instrument every backend service with OpenTelemetry via a new shared library,
`BuildingBlocks.Telemetry`**, mirroring the extraction pattern `BuildingBlocks.Messaging` already
established (ADR-0018): one `AddPlatformTelemetry(configuration, serviceName)` call per service,
wiring ASP.NET Core, HttpClient and EF Core instrumentation, an OTLP exporter, and a Loki sink added
alongside Serilog's existing Console sink. Every span and metric carries `service.name` and
`deployment.environment` (read from `ASPNETCORE_ENVIRONMENT`/`DOTNET_ENVIRONMENT`, not hardcoded) —
cheap to attach now, and the thing that will let the same dashboards later distinguish staging from
production data without being rebuilt once those environments exist.

**A single `otel-collector` container is the hub every service exports to**, fanning out to Tempo
(traces) and exposing a Prometheus-scrapeable endpoint (metrics), rather than each service exporting
directly to each backend. Logs are deliberately *not* routed through the collector — Serilog's own
Loki sink is simpler and already idiomatic for .NET; unifying all three signals through one pipe
would complicate the one path that already worked cleanly for no real benefit.

**Tempo is the trace store**, not Jaeger or Zipkin — the rest of the stack (Loki, Grafana, and
Prometheus for metrics) is already Grafana-native, and Tempo integrates with Grafana without a
separate query layer to wire up.

**Cross-service trace continuity through the outbox is solved by persisting, not assuming, trace
context.** `OutboxMessage` gained a nullable `trace_parent` column, captured from `Activity.Current?.Id`
by `OutboxWriter` at write time, while the writing request's context is still live. At dispatch time —
disconnected in time from that request — `BuildingBlocks.Messaging`'s own `ActivitySource` re-parents
a new Producer activity from that stored value (falling back to a fresh root activity if it's null,
e.g. a row written before this column existed) and injects standard W3C headers onto the outgoing AMQP
message via OpenTelemetry's own propagator, not a hand-rolled header format. Consumers — both the
DB-backed `InboxConsumerBase<TDbContext>` shape and NotificationService's hand-rolled, no-DbContext
`BalanceChangedConsumer` — extract those headers through the same shared helper and start a parented
Consumer activity, so the whole HTTP → outbox write → RabbitMQ publish → inbox consume → SignalR push
path now lands in Tempo as one trace, not four disconnected ones. This required registering the
library's manually-created `ActivitySource` explicitly via `AddSource` in `AddPlatformTelemetry` —
the auto-instrumentation packages only pick up their own sources, not arbitrary custom ones.

**Every authenticated request and every messaging span carries `enduser.id`** — the standard OTel
semantic-convention attribute, sourced from the same `JwtRegisteredClaimNames.Sub` claim already used
everywhere in this codebase, not a new identifier. `EnduserIdMiddleware` (mirroring
`CorrelationIdMiddleware`'s `LogContext.PushProperty` shape exactly) tags both the current Activity
and the Serilog log context in Identity/Economy/Notification; ApiGateway needed its own
`EnduserIdEnricher` hooked into Ocelot's `PreAuthorizationMiddleware`, since Ocelot authenticates
per-route outside the standard ASP.NET Core pipeline rather than through it. Messaging spans get the
tag only where the event payload already carries a user id (as `BalanceChangedConsumer`'s
`notification.UserId` already does for SignalR routing) — no schema was added solely to backfill this
on event types that don't already carry one.

**Retention on every backend is time-bounded and configurable, not left to grow forever.** Loki and
Tempo both load `-config.expand-env=true` so their retention windows read from `.env`
(`LOKI_RETENTION_PERIOD` / `TEMPO_RETENTION_PERIOD`, both defaulting to `4380h`, ~6 months); Loki also
needed an explicit `compactor` block with `retention_enabled: true`, since `limits_config.retention_period`
alone is silently ignored without it. Prometheus previously ran on the image's own default command with
no retention flag at all (a 15-day default) — it now has `--storage.tsdb.retention.time` explicitly set
from `PROMETHEUS_RETENTION_PERIOD` (default `180d`) alongside the rest of the image's standard flags.

## Alternatives considered

| Alternative | Why it was not chosen |
|---|---|
| Each service exports OTLP directly to Tempo/Prometheus, no collector | Every service would need to know every backend's address and handle its own batching/retry; a collector centralizes that once |
| Jaeger or Zipkin for trace storage | The rest of the stack is already Grafana-native (Loki, Grafana, Prometheus); Tempo needs no separate query layer wired into Grafana |
| Route logs through the collector too, unifying all three signals through one pipe | Serilog → Loki already works simply and idiomatically for .NET; the extra indirection would buy nothing |
| Leave the outbox/messaging path untraced, accept a broken trace at that boundary | Async messaging is a core part of this platform's architecture (ADR-0010); an observability story that stops at the HTTP boundary would miss the exact part of the system hardest to debug by hand |
| A custom JSON field carrying trace context in the message body, instead of AMQP headers | Headers are the standard carrier for this (mirrors how HTTP does it) and are exactly what OpenTelemetry's own propagator already knows how to inject/extract — no reason to invent a parallel mechanism |
| Add a dedicated user-id column to `OutboxMessage` to guarantee `enduser.id` tagging on every messaging span uniformly | Most user-scoped events already carry a user id in their own payload for unrelated reasons (SignalR routing); adding schema solely for telemetry tagging on events that don't already have one is scope beyond what was asked |
| Leave Loki/Tempo/Prometheus retention at their defaults (unlimited / 24h / 15d) | Wildly inconsistent between the three, and unlimited retention on a long-running host is exactly the "runs out of disk" failure mode this is meant to avoid |

## Consequences

### Benefits

A request's entire path — including the asynchronous half most systems can't show at all — is one
trace, filterable by the specific player it belongs to, in both Tempo and Loki. Every new service
gets this by referencing one library and calling one method, not by re-deriving instrumentation,
export configuration, or log enrichment per service. Retention is a `.env` value, not a redeploy.

### Trade-offs

**Deploy-coupling**, the same shape ADR-0018 already accepted for `BuildingBlocks.Messaging`: a change
to `BuildingBlocks.Telemetry` touches every consuming service, not just one. Kept small and
infrastructural for the same reason — instrumentation wiring only, never anything domain-specific.

**The collector, Tempo, Prometheus, Loki and Grafana were, at the time of this decision, only running in
`infra/docker-compose.yml`.** That gap closed once the platform moved onto Kubernetes — see
[ADR-0021](0021-kubernetes-helm-migration.md) for the chart migration and
[ADR-0022](0022-observability-stack.md) for how this exact stack maps onto it (RBAC for
node-exporter/kube-state-metrics/cadvisor, a real Grafana Viewer account, and the rest).

**Frontend instrumentation is not part of this decision.** Browser-side OpenTelemetry (propagating
trace context from the client through the gateway) is a deliberate follow-up, not bundled here,
since there is nothing for it to join into until the backend half of the trace exists.

### When to revisit

If a service needs a materially different export destination (a managed tracing backend instead of
self-hosted Tempo, say), the collector is exactly the layer that absorbs that change without
touching any service's own code. If a sixth service or a materially different messaging shape
(ordered delivery, a dead-letter queue) needs trace propagation this mechanism doesn't already cover,
extend `MessagingTracePropagation` rather than hand-rolling a parallel path.
