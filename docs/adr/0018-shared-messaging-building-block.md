# ADR-0018: Shared messaging building block (`BuildingBlocks.Messaging`)

- **Status:** Accepted
- **Date:** 2026-07-24

## Context

ADR-0010 gave EconomyService a transactional outbox behind `IEventBus`: a topic exchange, a polling
dispatcher with `SELECT ... FOR UPDATE SKIP LOCKED`, and a deduplicating inbox-lite consumer. Slice 3
adds a second producer (IdentityService, publishing `UserEmailConfirmed` for the welcome-grant
choreography) and at least two more consumers (NotificationService reacting to `BalanceChanged`;
InventoryService on both sides of the purchase saga). Every one of those needs the same mechanism:
write an outbox row in the same transaction as the domain change, dispatch it with the same
claim-and-retry loop, and deduplicate inbound deliveries the same way.

Copying `EconomyService/Messaging/*` and `EconomyService/Inbox/*` into three more services would leave
3-4 diverging copies of one pattern, each free to drift on retry limits, table names, or the dedup
transaction shape. None of that copying would touch anything domain-specific — the outbox row, the
RabbitMQ connection, the topic exchange, and the dedup ledger don't know or care what `BalanceChanged`
or `UserEmailConfirmed` mean.

## Decision

**Extract the mechanism into a class library, `BuildingBlocks.Messaging`, referenced by every service
that publishes or consumes integration events.** EconomyService is the first consumer, moved onto the
library in the same group that creates it, with no change to its observable behavior — every existing
messaging/outbox/inbox test keeps passing unmodified.

**What the library owns (infrastructure only):**
- Publish side: `IEventBus`, `RabbitMqEventBus`, `IRabbitMqConnection`/`RabbitMqConnection`,
  `RabbitMqTopologyInitializer`, `RabbitMqOptions`, `EventEnvelope`, the abstract `IntegrationEvent`
  record.
- Outbox: the `OutboxMessage` entity and its `IEntityTypeConfiguration`, `IOutboxWriter`/
  `OutboxWriter<TDbContext>`, `OutboxDispatcherOptions`, `OutboxDispatcherService<TDbContext>`.
- Inbox: the `ProcessedMessage` dedup-ledger entity and its configuration, `IInboxFaultInjector`/
  `NoOpInboxFaultInjector` (the crash-before-commit test seam), and a generic template-base consumer,
  `DeduplicatingConsumerBase<TDbContext>`, that owns the connect/bind/receive loop and the
  insert-processed-message-then-apply-side-effect-then-commit transaction shape. A consuming service
  derives from it and implements only the side effect.
- DI wiring: `AddRabbitMqEventBus(configuration)`, `AddOutbox<TDbContext>()`,
  `AddOutboxDispatcher<TDbContext>(configuration)`.

**What stays with each service (domain):** the integration event types themselves
(`BalanceChangedEvent`, `UserEmailConfirmed`, and so on — a consumer reads them as a tolerant reader,
never by referencing a shared contract type), every domain handler and side effect (EconomyService's
own `ProjectedEventCount` projection, for instance), and, critically, the database. **No service shares
a `DbContext`, a connection string, or a table with another.** The library hands out an entity type and
a Fluent configuration; each service applies that configuration to its own `DbContext`, generates its
own migration, and owns its own `outbox_messages`/`processed_messages` in its own database. ADR-0001
(database per service) is unaffected — this is shared code, not shared data.

**Both the writer and the dispatcher are generic over `TDbContext : DbContext`, not merely `DbContext`.**
This is not a style preference: `EconomyService.LedgerService` opens a database transaction, writes a
ledger entry, and calls `IOutboxWriter.WriteAsync` *inside that same transaction*, relying on EF Core
enlisting the outbox insert into the already-open transaction because it runs on the same scoped
`DbContext` instance. If the library resolved a different `DbContext` instance than the one the caller
is transacting against, the dual-write guarantee this whole mechanism exists for would break silently.
Making the writer and dispatcher generic lets DI resolve the exact service-owned `DbContext` type into
the same scope, with no behavior change from today.

**The generic consumer base is a template method, not a thin helper.** An earlier option considered
extracting only the dedup transaction (insert `ProcessedMessage`, catch the primary-key conflict, apply
a side effect, commit) as a static helper, leaving the RabbitMQ connect/bind/receive loop to be
rewritten per service. That would still leave the loop itself — the larger and more error-prone part —
duplicated across Notification and Inventory, which defeats the reason this library exists. The template
base owns the loop; each service overrides one method.

**No dedicated test project for the library.** The library's generic code is exercised through
EconomyService's own existing integration tests (`OutboxDispatcherServiceTests`,
`ConsumerDeduplicationTests`, `RabbitMqTopologyTests`) run against its real `EconomyDbContext` and a
real broker — that coverage only grows as Notification and Inventory become second and third
consumers with their own integration tests. Building a standalone test harness with a synthetic
`DbContext` now would test a scenario nothing in the system actually has.

## Alternatives considered

| Alternative | Why it was not chosen |
|---|---|
| Copy `Messaging/`+`Inbox/` into each new service | Exactly the 3-4 diverging copies this ADR exists to avoid; any future fix (a retry-count bug, say) would need to land in every copy separately |
| Shared library, but only the dedup transaction extracted (thin helper, no consumer base class) | Leaves the RabbitMQ connect/bind/receive loop — the largest, most repetitive part — duplicated in every consumer |
| Shared library with a non-generic `OutboxWriter`/`OutboxDispatcherService` resolving `DbContext` directly | Cannot guarantee the same scoped instance a caller is already transacting against; risks a silent dual-write break |
| Adopt a messaging framework (e.g. MassTransit) instead of extracting the hand-written mechanism | Rejected already by ADR-0010 for the same reason: a framework would hide the exact mechanism this project exists to demonstrate. Extraction keeps the manual implementation, just shared |
| A dedicated `BuildingBlocks.Messaging.Tests` project with a synthetic `DbContext` | Tests a scenario no real consumer has yet; EconomyService's existing integration tests already exercise the generic code path through a real `DbContext` |

## Consequences

### What we get

One outbox/inbox implementation instead of a diverging one per service. A new producer or consumer
(Identity, Notification, Inventory) gets the mechanism by referencing the library and implementing only
its own domain event and side effect, not by re-deriving the transaction shape, the retry policy, or the
RabbitMQ topology declaration. The `TDbContext` generic parameter keeps the dual-write guarantee intact
without any service sharing a database connection or context with another.

### What it costs

**Deploy-coupling.** A change to `BuildingBlocks.Messaging` requires rebuilding and retesting every
consumer, not just the service that changed — the opposite of the per-service path-filtered CI this
project otherwise relies on. CI compensates with a dedicated path filter: a change under
`BuildingBlocks.Messaging/**` retriggers every consuming service's pipeline, not just one.

The library is kept deliberately small and infrastructural — transport and dedup mechanics only, never
a domain event or a domain handler — specifically to keep that deploy-coupling blast radius bounded.
Growing it to hold anything domain-specific would make the coupling cost worse than the duplication it
replaces.

### When this gets revisited

If a fourth or fifth service needs a materially different delivery guarantee (exactly-once, ordered
delivery, a dead-letter queue) that the current at-least-once/no-DLQ model (ADR-0010) can't express
without per-consumer special-casing, the generic base may need a second variant rather than more
constructor parameters bolted onto this one. If the deploy-coupling cost becomes disproportionate to the
duplication it saves — unlikely at four consumers, plausible at ten — revisit whether some consumers
should fork their own copy instead of tracking the shared one.
