# ADR-0010: Transactional outbox behind an `IEventBus` abstraction

- **Status:** Accepted
- **Date:** 2026-07-19

## Context

ADR-0003 decided EconomyService reacts to its own state changes by publishing events, rather than
calling other services synchronously. That creates a dual-write problem: a ledger entry gets committed
to Postgres, and an event about it needs to reach RabbitMQ, and those are two different systems with no
shared transaction between them.

Doing "commit the ledger entry, then publish" loses the event entirely if the process crashes between
the two steps. Doing "publish, then commit" can publish an event for a change that never actually
happens, if the transaction rolls back afterward. Neither ordering is safe on its own.

## Decision

**Transactional outbox.** The event is written to an `outbox_messages` row inside the *same* database
transaction as the ledger entry it describes. A separate background dispatcher polls that table and
relays unsent rows to RabbitMQ, marking each as sent once the broker has acknowledged it. Because the
event row and the ledger entry commit or roll back together, there is no window where one exists without
the other.

**`IEventBus` as the publish-side interface.** `LedgerService` calls `IEventBus.PublishAsync(event)`
without knowing that RabbitMQ.Client sits behind it. This buys two things concretely: the publish call
can be substituted with a test double in unit tests without a broker, and the underlying client library
can be swapped later without touching call sites in the domain services.

**Manual implementation, not MassTransit or a similar framework.** The outbox table, the dispatcher, and
the RabbitMQ topology are hand-written rather than delegated to an off-the-shelf messaging framework.
This is a deliberate portfolio choice: a framework would hide the mechanism this ADR exists to
demonstrate. The cost is that this is more code to maintain than `services.AddMassTransit(...)` would be.

**What `IEventBus` does *not* buy — stated plainly, since it's easy to overstate this.** The interface
abstracts the call site, not the delivery mechanism. A framework like MassTransit has its own outbox
implementation (typically an EF `SaveChanges` interceptor, not a polling background service) and its own
publish surface (`IPublishEndpoint`/`IBus`), and it does not plug into an arbitrary `IEventBus.PublishAsync`
from outside. Adopting MassTransit later would mean replacing the custom outbox table and dispatcher
outright, not sliding a MassTransit-backed implementation in behind this interface. `IEventBus` makes the
RabbitMQ client swappable and the publish call mockable — it does not make the outbox mechanism itself
framework-agnostic.

**Delivery guarantee: at-least-once.** The dispatcher can publish a message and crash before marking it
sent, causing redelivery on restart. Consumers are responsible for idempotent handling (an inbox-style
dedup table, tracked as a Group 4 concern) — this ADR does not attempt exactly-once delivery.

**Dispatcher concurrency.** Rows are claimed with `SELECT ... FOR UPDATE SKIP LOCKED`, so if the service
is ever run as multiple replicas, each dispatcher instance claims a disjoint set of unsent rows instead
of racing to publish the same one twice. State that decides "who publishes this row" lives in the
database, not in any one process's memory — the same reason the in-process rate limiter from slice 1
isn't authoritative across replicas.

**Topology.** A topic exchange (`gbp.economy`), with a routing key per event type (e.g.
`balance.changed`). Topic, rather than fanout or direct, so a future consumer can subscribe to a subset
of event types without the exchange being redeclared. Exchange, queue, and bindings are declared
idempotently at service startup.

**Retry and failure handling.** Transient publish failures retry via Polly with backoff. After a maximum
attempt count, a row is left unsent with its attempt count at the ceiling and is logged, rather than
retried forever — there is no dead-letter queue or poison-message handling in this slice; a stuck row is
surfaced through logs and `attempts`, not automatically quarantined.

## Consequences

**Gained:** no lost events across the dual-write boundary; a testable publish interface; replicas that
don't double-publish.

**Given up / accepted:**
- **Polling delay**, not push — an event waits for the dispatcher's next poll rather than being relayed
  instantly.
- **The dispatcher is a moving part** the project now runs and monitors, on top of the database and the
  broker.
- **At-least-once pushes the dedup problem onto consumers** (Group 4's concern, not solved here).
- **No DLQ / poison-message handling** — named limitation, not built in this slice.
- **`IEventBus` swaps the broker client, not the outbox mechanism** — see above; adopting a framework
  like MassTransit later is a replacement of this component, not an extension of it.


## Addendum: the conversion saga

### Context

Slice 2 needs a saga with a compensating action to demonstrate that pattern. A full cross-service
saga isn't possible yet — InventoryService doesn't exist until slice 3 — so the demonstration has to
be self-contained. Currency conversion (platform balance → game balance, by rate) is the natural
candidate: both currencies already live in EconomyService.

### Decision

**The conversion saga is in-process and sequential, not choreography over the message bus.** A debit
transaction (platform currency) commits, then a credit transaction (game currency) commits; if the
credit step fails, a compensating transaction reverses the debit. Each transition commits its own
step and is recorded as a status on `conversion_requests` (`Started → DebitDone → Completed`, or
`DebitDone → Compensating → Failed`), so a crash mid-saga leaves a durable, inspectable state rather
than an ambiguous one.

This is deliberately not built as choreography over RabbitMQ. Both steps belong to the same service
and there is no second participant reacting to an event — routing this through the bus would be
coordination theater, not genuine choreography, and it would make the compensating path
non-deterministic to test (a failure injected mid-flight would be racing an async delivery instead of
being a straightforward step in a sequential call). The outbox events this saga emits
(`ConversionDebited`, `ConversionCompletedEvent`, `ConversionFailedEvent`) are for observers — the
deduplicating consumer added in this same group is one such observer — they do not drive the saga's
own steps.

The API is asynchronous regardless: `POST /conversions` returns `202 Accepted` with a `Started`
status, and the client polls `GET /conversions/{id}`. Execution happens on a background runner fed by
an in-process channel, not inline in the request — the request returning doesn't mean the saga
finished, only that it was accepted.

The exchange rate is snapshotted onto the `conversion_request` at creation (`rate_applied`), so a rate
change while a conversion is in flight never affects a conversion already in progress.

### Consequences

**Gained:** a compensating-action saga that is deterministic to test — the happy path and the
compensating path are both just sequential method calls against a real database, not a race against
async delivery.

**Given up / accepted:**
- This is not genuine cross-service choreography — a real second participant reacting to an event is
  the slice 3 story, once InventoryService exists.
- No distributed transaction between the debit and credit steps; the compensating transaction is a
  business-level reversal, not a database rollback, and it runs after the failure is already visible.
- The client learns the outcome by polling, not by push notification.