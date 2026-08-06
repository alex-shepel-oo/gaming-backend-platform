# Messaging

EconomyService publishes an integration event for every state change a future
consumer might care about (`BalanceChangedEvent` and friends), using the
transactional outbox pattern rather than publishing to RabbitMQ directly from
the request path. See [ADR 0003](adr/0003-async-inter-service-communication.md)
for why events instead of a synchronous call,
[ADR 0010](adr/0010-transactional-outbox-event-bus.md) for the
outbox itself, and
[ADR 0018](adr/0018-shared-messaging-building-block.md) for why the
mechanism described below lives in a shared library rather than inside
EconomyService.

The publish/consume machinery itself — `IEventBus`, the outbox writer and
dispatcher, and the inbox dedup base — lives in `BuildingBlocks.Messaging`,
a library shared across services rather than code specific to EconomyService.
The library's boundary is infrastructure only: transport, topology, and the
generic outbox/inbox entities and dispatch/consume mechanics. Domain events
(`BalanceChangedEvent` and friends) and domain side effects (the
`ProjectedEventCount` projection below) stay in EconomyService — the library
never sees them. Each consuming service also keeps its own `outbox_messages`
and `processed_messages` tables in its own database; the library gives every
service the same code to work with, not a shared table, so ADR-0001 still
holds.

**Flow:** `LedgerService` writes an `outbox_messages` row in the same database
transaction as the ledger entry it describes — both commit together, or
neither does. A separate background service (`OutboxDispatcherService`) polls
that table for unsent rows, claiming them with `SELECT ... FOR UPDATE SKIP
LOCKED` so that if EconomyService is ever scaled to multiple replicas, no two
of them publish the same row. Each claimed row is relayed through `IEventBus`
to RabbitMQ and marked `processed_at` once the broker acknowledges it.

**Delivery guarantee:** at-least-once, not exactly-once. A crash between
publishing and marking a row processed causes that message to be redelivered
on the next poll. Deduplicating a redelivered message is the consumer's job -
see below.

**Topology:** a topic exchange named `gbp.economy`, with the routing key set
to the event's type (e.g. `balance.changed`). Topic rather than fanout or
direct, so a consumer added later can bind to just the event types it needs
without the exchange being redeclared. The exchange is declared idempotently
each time the service starts.

## Consumer and inbox-lite deduplication

EconomyService also binds a queue to its own exchange (`balance.changed` and
the three `conversion.*` routing keys) and consumes what it publishes. This
is a demonstration of the delivery loop surviving redelivery, not a
production subscriber - no other service reads these events yet.

Before doing anything with a delivery, the consumer inserts the message's id
into `processed_messages` and applies the delivery's side effect (a
projection counter) in the *same* database transaction. A primary-key
conflict on that insert means an earlier delivery already got here, so the
message is acked and skipped without reprocessing; a crash between the
insert and the commit rolls both back together, so a redelivered message is
reprocessed cleanly rather than silently lost. This is deliberately
**inbox-lite**, not a full inbox pattern - there's no per-message retry
bookkeeping or metadata beyond `message_id` and `processed_at`.

**Known limitations** (of the shared mechanism, so they apply to every
consumer of `BuildingBlocks.Messaging`, not just EconomyService):
- No dead-letter queue. A row that keeps failing to publish is parked once
  its attempt count hits the configured ceiling — left unsent, logged, and
  no longer retried — rather than routed anywhere for inspection.
- Not exactly-once. See the delivery guarantee above.
- The dispatcher polls on an interval rather than reacting to commits via
  logical replication/CDC, so there is always some delay between a ledger
  entry landing and its event reaching the broker.
- Both IdentityService and EconomyService now fail to start without a
  reachable broker — RabbitMQ went from an EconomyService-only dependency
  to a platform-wide one the moment Identity got its own outbox.

## Welcome grant

IdentityService has its own `outbox_messages` table and its own exchange (`gbp.identity`), populated the
same way EconomyService's is — confirming an email writes a `UserEmailConfirmed` row in the same call
that flips `EmailConfirmed`, no separate transaction needed since that call already goes through one
`SaveChangesAsync`. EconomyService's `UserEmailConfirmedConsumer` binds to that exchange directly — the
first consumer in the system that isn't reading its own service's events — and grants a starting
`PLATFORM_CREDITS` balance through the existing `ILedgerService.GrantAsync`, keyed on
`welcome:{userId}` so a redelivery replays instead of double-granting. Seeded demo users
(`admin`, `player.one`, `player.two`, `gameadmin@demo-racer.dev`, `player.three`) never go through
register/confirm-email, so they get the same balance directly from `EconomyService.DevelopmentSeeder`
instead, addressed by a fixed `UserId` IdentityService's own seeder now assigns them (the same
no-real-foreign-key convention already used for the seeded game ids).

Binding a queue to another service's exchange needed one small change to the shared library:
`InboxConsumerBase<TDbContext>` used to read the exchange to bind from the same `RabbitMqOptions` a
service publishes with, which only ever worked because the one consumer that existed listened to its
own exchange. It now takes the exchange as an explicit argument instead. Full reasoning in
[ADR 0010's welcome-grant addendum](adr/0010-transactional-outbox-event-bus.md#addendum-the-welcome-grant-and-identitys-first-outbox).
