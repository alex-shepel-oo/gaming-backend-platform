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