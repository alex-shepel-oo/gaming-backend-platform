# BuildingBlocks (shared libraries)

Three shared libraries every backend service references, not services in their own right — no
independent deployment, no independent API.

## BuildingBlocks.Auth

JWKS validation shared by every service that needs to validate a JWT: `JwksKeyCache`/
`JwksKeySnapshot` hold the current public key set, `JwksRefreshHostedService` polls IdentityService's
`/.well-known/jwks.json` on an interval and swaps the cache atomically, so a key rotation doesn't
require restarting every downstream service. Extracted out of duplicated per-service code — before
this existed, IdentityService, EconomyService, and ApiGateway each wired up their own copy of the same
JWKS-fetching logic. See [ADR 0017](../adr/0017-rs256-and-jwks.md).

## BuildingBlocks.Messaging

The outbox/inbox pattern and RabbitMQ wiring every service that publishes or consumes events shares:
`IEventBus`/`RabbitMqEventBus`, `IOutboxWriter`/`OutboxWriter`/`OutboxDispatcherService` (the poll
loop that reads unprocessed `OutboxMessage` rows and publishes them, `SELECT ... FOR UPDATE SKIP
LOCKED` guarding against two dispatcher replicas racing the same row), `InboxConsumerBase<TDbContext>`
(dedup against a `ProcessedMessage` table before handling), and `MessagingTracePropagation` (carries a
trace's `trace_parent` across the async hop so a distributed trace doesn't dead-end at the outbox).
`RabbitMqTopologyInitializer` declares exchanges/queues at service startup — only the *publishing*
service's topology initializer declares its own exchange, which is why `email-service` in
`docker-compose.yml` has to wait on `identity-service` being healthy before it can bind its queues.
See [ADR 0010](../adr/0010-transactional-outbox-event-bus.md) and
[ADR 0018](../adr/0018-shared-messaging-building-block.md).

## BuildingBlocks.Telemetry

`AddPlatformTelemetry` wires OpenTelemetry (ASP.NET Core, HttpClient, EF Core instrumentation) and
Serilog identically across every backend service — one shared extension method, not a hand-rolled
setup per service. Exports traces/metrics via OTLP to `otel-collector`; Serilog keeps its
correlation-ID-enriched Console sink and adds a Loki sink. `OtelConventions` centralizes the shared
attribute names (`enduser.id` foremost among them) so every service tags spans identically rather than
each picking its own key. See [ADR 0019](../adr/0019-opentelemetry-observability.md) and
[Observability overview](../observability/overview.md).

## Related documentation

- [ADR 0010: Transactional outbox](../adr/0010-transactional-outbox-event-bus.md)
- [ADR 0017: RS256 + JWKS](../adr/0017-rs256-and-jwks.md)
- [ADR 0018: Shared messaging building block](../adr/0018-shared-messaging-building-block.md)
- [ADR 0019: OpenTelemetry observability](../adr/0019-opentelemetry-observability.md)
- [Messaging](../messaging.md)
