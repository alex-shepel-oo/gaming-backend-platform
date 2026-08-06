# ADR-0001: Database per service

- **Status:** Accepted
- **Date:** 2026-07-16

## Context

The platform is designed as several independent bounded contexts (Identity, Economy, Inventory,
Marketplace, Validation, Notification), each its own `.csproj`, its own Dockerfile, its own
deployment path, its own pace of schema change. The path-filtered CI (see `docs/architecture.md`)
already assumes that a change in one service should not require rebuilding, retesting, or
redeploying the rest.

A shared database across several services creates hidden coupling: any service can read or write
a table it does not formally own, one service's schema cannot change without coordinating with
every other, and one service's migration can be blocked by another service's transactions on the
same database.

## Decision

Each service owns its own PostgreSQL 17 database and has no direct access to any other service's
database. IdentityService → `identity_db`, EconomyService → `economy_db`, and so on for every
service. Each database's schema is created and versioned by that service's own EF Core
migrations — never a shared init script, hand-written SQL, or migrations applied from outside
the owning service.

Data crosses service boundaries through public APIs (synchronous) or through events on RabbitMQ
(asynchronous, choreography-based saga) — never through a direct SQL query against another
service's schema.

In local development and CI, each database is a separate Postgres container on its own port; in
Kubernetes, a separate StatefulSet with its own PVC in the namespace. Production ultimately landed
on a self-hosted VPS running k3s rather than Azure, the target this ADR originally scoped for
([ADR-0021](0021-kubernetes-helm-migration.md)) — but the "one database per service" principle
carried over unchanged: still a StatefulSet per service, never one shared schema serving several.

## Alternatives considered

| Option | Why not |
|---|---|
| Shared database, split by schema (`identity.*`, `economy.*` in one Postgres) | Cheaper to operate at MVP scale, but does not protect against coupling at the code level: nothing stops EconomyService from joining `identity.users` by accident, and one service's migration physically locks the same database another service depends on |
| Shared database, split by application-level convention with no physical schema separation | Same problem, without even a formal boundary — the worse of the two |
| Shared read-model / CQRS projection for cross-service queries | A reasonable pattern, but it answers a different question (how services read each other's aggregated data), not an alternative to storage isolation. It complements this ADR when needed rather than replacing it |

## Consequences

### What we get

Every service deploys, tests, and scales independently. One service's schema changes without
coordinating with the others. An incident or defect in one database does not require stopping
the rest. Path-filtered CI (a change under `backend/EconomyService/**` does not touch
`IdentityService`) becomes meaningful rather than cosmetic — with a shared database it would
still require running migrations for every service.

### What it costs

Cross-service queries that would be a `JOIN` in a monolith become an HTTP call or an event —
more expensive in latency and harder to handle for partial failure (see the Polly policies on
inter-service calls, and ADR-0003 for the sync/async choice). Transactional consistency across
services is not achievable — hence a saga instead of a distributed transaction. Local development
runs several database containers instead of one (one, `identity_db`, for slice 1; up to six once
the full service set exists).

### When this gets revisited

If the number of services — and, correspondingly, the number of managed databases — makes the
operational load disproportionate to the benefit of isolation, a shared instance split by schema
becomes worth considering as a compromise between isolation and operating cost, as long as the
logical boundary (each service writes only to its own schema) is preserved without physically
separating servers.