# EconomyService

## Responsibility

Owns currencies, balances, and the conversion path between a platform-wide currency and any game's
own currency. Every balance change — grants, spends, adjustments, both legs of a conversion — is a
`LedgerEntry`, not a mutation of a running total alone; the running total is derivable from the
ledger, not the other way around.

## Architecture

Minimal APIs (`BalanceEndpoints`, `CurrencyEndpoints`, `ConversionEndpoints`, `TransactionEndpoints`,
`HealthEndpoints`). Owns `economy_db` outright — see [Data ownership](../architecture/data.md).
Conversions run through an in-process saga (`ConversionSaga`/`ConversionSagaRunner`, backed by a
channel) rather than a single transaction spanning both legs, since debiting one currency and
crediting another are two separate ledger writes that need to either both land or compensate, not one
atomic write.

## API

Full endpoint reference: [Economy API](../api/economy.md). Route groups: balances (`/balances/me`,
platform-admin balance adjustment), currencies (catalog), conversions (start, poll status), and
transaction history (filterable by currency and type, exposes the idempotency key so a conversion's
two ledger legs can be paired by caller).

## Data

Owns `economy_db`: `Currencies`, `Balances`, `LedgerEntries`, `ConversionRates`, `ConversionRequests`,
plus its own `OutboxMessages`/`ProcessedMessages` for the outbox/inbox pattern. See
[Data ownership](../architecture/data.md) for the cross-service boundary and what it costs (the
welcome-grant round trip, the game-hard-delete orphaning trade-off).

## Messaging

Consumes `UserEmailConfirmedEvent` from IdentityService (`UserEmailConfirmedConsumer`, deduplicated
via `DeduplicatingEventConsumer` against `ProcessedMessages`) to grant a new player's starting
platform balance. Publishes `BalanceChangedEvent` on every ledger-affecting write — the event
NotificationService relays live to a connected client over SignalR. See
[Messaging](../messaging.md) and [ADR 0010](../adr/0010-transactional-outbox-event-bus.md).

## Dependencies

`api-gateway` in front, RabbitMQ for both directions, `economy_db` (Postgres). No synchronous call
into IdentityService — it trusts the `GameId`/`UserId` claims a caller's JWT already carries rather
than validating them against IdentityService on every request.

## Security

Same JWT/JWKS validation every service shares ([ADR 0017](../adr/0017-rs256-and-jwks.md)), permission
claims enforced per endpoint (`platform.balance.adjust` for admin-initiated grants/adjustments,
ordinary player scope for self-service balance/conversion reads). No rate limiting here — unlike
IdentityService's auth surface, nothing on this service is reachable pre-authentication. Full detail:
[Security overview](../security/overview.md).

## Deployment

Docker image + `economy-migrator` one-shot migration container/Job. StatefulSet-backed `economy-db`
locally and in Kubernetes (dev/sandbox only). See [Backend deployment topology](../architecture/backend.md).

## Observability

OpenTelemetry traces/metrics via `BuildingBlocks.Telemetry`, Serilog to Console + Loki, `/health` and
`/health/ready`. A trace touching the outbox → RabbitMQ → consumer path (welcome grant,
`BalanceChanged` → NotificationService) carries `trace_parent` across the async hop rather than
starting a disconnected trace on the consumer side. See [Observability overview](../observability/overview.md).

## Related documentation

- [Economy API reference](../api/economy.md)
- [Data ownership](../architecture/data.md)
- [Messaging](../messaging.md)
- [ADR 0010: Transactional outbox](../adr/0010-transactional-outbox-event-bus.md)
- [ADR 0014: NotificationService and SignalR](../adr/0014-notification-service-and-signalr.md)
