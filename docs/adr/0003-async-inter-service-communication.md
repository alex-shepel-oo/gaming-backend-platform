# ADR-0003: Asynchronous events for inter-service reactions, synchronous HTTP where a caller must wait

- **Status:** Accepted
- **Date:** 2026-07-19

## Context

Slice 1 established database-per-service (ADR-0001): no service can read another's schema, so state
changes cross service boundaries only through an explicit call. Slice 2 introduces the first case where
one service's state change needs to cause work in another — most concretely, a balance change in
EconomyService that a future notification or analytics consumer needs to react to, and, later, a
cross-currency conversion that spans EconomyService and a dedicated worker.

The two ways to make that happen are a direct synchronous HTTP call between services, or an
asynchronous message the origin service emits without waiting for a reaction.

## Decision

**Asynchronous events, published via a message broker, are the default for inter-service reactions to a
state change.** Synchronous HTTP stays for the cases where a caller genuinely cannot proceed without a
reply — API Gateway routing to whichever service owns a request, and any read where the client needs the
current state, not a notification that state changed.

Reasons:
- **Temporal decoupling.** The producer does not need the consumer to be up, reachable, or fast. A
  balance-changed event can be emitted whether or not anything is currently listening for it.
- **No distributed transaction.** A synchronous call from EconomyService to another service, made from
  inside the same transaction that posts a ledger entry, would need the far side to also commit or the
  whole operation to roll back — which database-per-service does not support without two-phase commit
  machinery this project isn't taking on.
- **Independent deploys and scaling.** A consumer can be down for a deploy, or scaled independently,
  without the producer's request path ever noticing.
- **Failure isolation.** A consumer being unreachable is a delivery problem for the message, not a
  request failure for the user who triggered the original action.

This decision only concerns *reactions* to something that already happened. Conversion (slice 2, Group 4)
is a multi-step process with its own state machine — it is built as an asynchronous saga, not as a
synchronous call chain, for the same reasons above plus the fact that a currency conversion can take
longer than an HTTP client should be made to wait.

## Consequences

**Gained:** the properties above — services can fail, deploy, and scale independently of whoever
consumes their events.

**Given up / accepted:**
- **Eventual consistency.** A consumer's view of another service's state lags behind the producer by
  however long delivery takes. Nothing in slice 2 requires a consumer to see a change instantly.
- **Cross-boundary tracing gets harder.** A request that used to be one HTTP call with one trace is now a
  publish and a separate, later consume. Correlation IDs are propagated through the event payload for
  exactly this reason (see the correlation-id middleware already in place on each service).
- **At-least-once delivery.** A message can be delivered more than once (see ADR-0010). Consumers are
  responsible for being idempotent — this is not something the transport layer solves for them.
- **Operational cost.** A broker is now infrastructure the project runs and depends on, on top of each
  service's own database.