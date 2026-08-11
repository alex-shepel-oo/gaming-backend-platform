# NotificationService

Port 5003, no database — the service holds nothing that needs to outlive a restart, so a missed push
just leaves a client's balance at its last known value until something else refreshes it. The one thing
it does is turn `BalanceChanged` — already published by EconomyService onto `gbp.economy` for every
ledger-affecting change (see [Messaging](../messaging.md) and
[ADR 0010](../adr/0010-transactional-outbox-event-bus.md)) — into a live push toward whichever browser
tab happens to be connected. EconomyService's publishing side needed no changes to make this work. Full
reasoning in [ADR 0014](../adr/0014-notification-service-and-signalr.md).

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/hubs/notifications/negotiate` | bearer | SignalR negotiate; token read from the `Authorization` header, same as every other endpoint |
| GET | `/hubs/notifications` | bearer, token via `access_token` query parameter on this path only | WebSocket/long-polling transport for the hub — a WebSocket handshake can't carry custom headers, so this is the one place in the service that reads a token out of the URL |
| GET | `/health` | anonymous | Liveness probe |
| GET | `/health/ready` | anonymous | Readiness probe (RabbitMQ only — no database to check) |

Auth validates against Identity's published JWKS the same way every other service does
([ADR 0017](../adr/0017-rs256-and-jwks.md)), but addressed delivery needs one
extra piece: a custom `IUserIdProvider`. SignalR's default implementation reads
`ClaimTypes.NameIdentifier`, while every token issued in this platform carries the caller's id under the
short claim name `sub` (`MapInboundClaims = false`, already the convention IdentityService and
EconomyService both use). Leave the default provider in place and `Clients.User(id)` matches no one — the
connection still authenticates, the push simply never shows up, and nothing logs an error to point at
why. Worth stating plainly here, since it's the kind of gap that looks fine until someone notices
deliveries aren't landing.

`BalanceChangedConsumer` consumes the queue directly through `IRabbitMqConnection` as a plain
`BackgroundService`, bypassing `BuildingBlocks.Messaging`'s `InboxConsumerBase<TDbContext>`
entirely — that base class ties its dedup step to a database transaction, and this service was built
without one on purpose. There's no dedup layer here at all: if a message gets redelivered, the consumer
just pushes the same, still-current balance a second time, which a connected client re-renders without
any visible effect.

The hub isn't reachable through Ocelot. An actual test against a running gateway showed the WebSocket
upgrade stalling for roughly fifteen seconds before Ocelot tore the connection down, with no SignalR frame
ever getting through — measured, not assumed, against the same request completing in under 200ms with no
proxy in the way. player-client's nginx now proxies `/hubs` straight to `notification-service:5003`
instead, the same direct-proxy approach already in place for `/api`
([ADR 0012](../adr/0012-frontend-security-and-guards.md)); see
[ADR 0014](../adr/0014-notification-service-and-signalr.md) for the full experiment.

## Known limitations

- **Single replica, no backplane.** SignalR keeps connection state in memory, which doesn't survive
  across replicas without `Microsoft.AspNetCore.SignalR.StackExchangeRedis` or similar. `replicas: 1` is
  this slice's accepted scale, not an unaddressed gap.
- **No notification history.** A client that's disconnected when a balance changes only finds out on its
  next request — kept for Extended scope.
- **A redelivered event can push the same balance twice.** Harmless: the second push repeats a balance
  the client already has, so nothing visibly changes.
