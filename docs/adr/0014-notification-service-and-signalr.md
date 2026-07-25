# ADR-0014: NotificationService and real-time delivery over SignalR

- **Status:** Accepted
- **Date:** 2026-07-25

## Context

EconomyService already publishes `BalanceChanged` to `gbp.economy` for every ledger-affecting change
(ADR-0010) — nothing consumed it outside the service's own self-demonstration consumer. The player-client
learns about a balance change either from the response of the action that caused it, or by polling a
conversion until it reaches `Completed`. Slice 3 wants a genuine push-based example: a balance change
should reach an already-open browser tab without it asking.

This is also the first time anything in the platform holds a long-lived, stateful connection to a
browser, and the first time a service outside EconomyService consumes an event for real rather than as a
demonstration. Both of those raise questions the existing ADRs don't answer: how does a persistent
WebSocket authenticate, given the access token lives only in memory and the refresh cookie is scoped to
`/api/identity/auth` (ADR-0011/0012)? And how does a consumer with no database of its own fit into the
outbox/inbox model ADR-0010 and the shared messaging library assume a `DbContext` for?

## Decision

**A new, stateless NotificationService, port 5003, no database.** It owns nothing that needs to survive a
restart — a missed push just means the client's balance stays at its last known value until the next
change or the next manual load. This is a deliberate limitation, not an oversight: notification history
is out of scope for this slice.

**SignalR hub at `/hubs/notifications`, authenticated with the same JWT bearer scheme every other service
already uses.** The client (`@microsoft/signalr`) supplies the token via `accessTokenFactory`; the
library puts it in the `Authorization` header for negotiate/long-polling and, for the WebSocket
handshake specifically, in an `access_token` query parameter — the browser gives a WebSocket handshake no
way to carry custom headers, so this is the only transport SignalR has for that case, not a relaxation of
the in-memory-token model. The server reads that query parameter through `JwtBearerEvents.OnMessageReceived`,
**scoped strictly to the hub's own path** — nowhere else on this service reads a token from a query
string, keeping that exposure to exactly the one place it's structurally required.

**A custom `IUserIdProvider` is required, not optional.** SignalR's default provider reads
`ClaimTypes.NameIdentifier`; every service in this platform issues and validates tokens with
`MapInboundClaims = false`, so the claim that actually carries the user's id is the short JWT name `sub`.
Without overriding the provider, `Clients.User(id)` would resolve nobody — connections would authenticate
successfully and pushes would simply never arrive, with no error anywhere to point at the cause. This is
named here explicitly because it is the kind of gap that looks like working code until someone notices
nothing ever gets delivered.

**The consumer does not use `BuildingBlocks.Messaging`'s `DeduplicatingConsumerBase<TDbContext>`.** That
base class' entire dedup mechanism is a row written inside the same database transaction as the
consumer's side effect — it requires a `DbContext` by hard generic constraint, and NotificationService
deliberately has none. Building a database solely to reuse that base class would contradict the reason
this service has no database in the first place. Instead, NotificationService consumes directly against
the library's lower-level, database-free primitives (`IRabbitMqConnection`, `RabbitMqOptions`) with no
deduplication at all: a redelivered `balance.changed` just pushes the same, still-current balance a
second time, which is indistinguishable from nothing having gone wrong. The queue itself is durable and
named, not exclusive or auto-delete — this is a real subscriber, not the self-consumption demo
EconomyService's own consumer explicitly disclaims itself to be, so a brief restart between deploys
shouldn't drop a backlog of pushes, even though nothing tracks which of them were already delivered.

**Single replica, no backplane.** SignalR's in-memory connection tracking doesn't survive across replicas
without `Microsoft.AspNetCore.SignalR.StackExchangeRedis` or an equivalent backplane. `replicas: 1` sits
alongside the reasoning already accepted for Platform.Worker. Redis-backplane is the named next step, not
a silent gap.

**Gateway routing for the hub is verified empirically, not assumed.** Whether Ocelot 24.1.0 proxies a
WebSocket upgrade cleanly is an open question as of this decision, deliberately not guessed at here — the
implementation work that follows either confirms Ocelot handles it and adds the route, or falls back to
nginx fronting `/hubs` directly (the same direct-proxy pattern ADR-0012 already uses for `/api`). Whichever
it turns out to be gets recorded here as a short follow-up note once known, not assumed in either
direction ahead of time.

## Alternatives considered

| Alternative | Why it was not chosen |
|---|---|
| Give NotificationService its own database, purely to reuse `DeduplicatingConsumerBase<TDbContext>` | Directly contradicts the reason the service has no database — trading a real architectural decision for reuse convenience |
| A full inbox-dedup table without a database (e.g. in-memory dictionary) | Doesn't survive a restart, and the delivery this service makes is already idempotent by nature — building persistence-shaped dedup for a case that doesn't need exactly-once is solving a problem this service doesn't have |
| Trust SignalR's default `IUserIdProvider` | Silently wrong given `MapInboundClaims = false` is already the platform-wide convention — would have shipped a feature that looks connected but never delivers |
| Accept the access token as a request header only, refuse WebSocket | Removes the query-string exposure, but WebSocket is the transport actually worth demonstrating here, and the query-token scope is already narrowed to the one path that needs it |

## Consequences

### What we get

A genuine push-based feature with no polling on the happy path, authenticated the same way as every
other service, without inventing a second auth scheme for real-time transport. The consumer pattern this
group establishes — library transport primitives without the DB-coupled dedup layer — is now available to
any future consumer that, like this one, has no database of its own.

### What it costs

No notification history — a client that's offline when a balance changes gets nothing until it next asks.
No horizontal scaling of the hub without a backplane this slice doesn't build. A redelivered event can
push the same balance twice in quick succession — harmless, but a real, named trade-off, not an
oversight. The access token appears in a URL query string for the one WebSocket handshake path, which is
a slightly wider exposure (proxy logs, browser history) than the header-only model every other endpoint
uses, accepted because the browser gives WebSocket no alternative.

### When this gets revisited

Redis backplane, once the hub needs to run more than one replica. A real inbox/notification-history table,
if a client needs to see what it missed while disconnected rather than only its current state. RS256/JWKS
(ADR-0008's named next step) applies here the same as everywhere else once it lands.
