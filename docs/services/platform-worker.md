# Platform.Worker

## Responsibility

Housekeeping: deletes rows that are already dead by their owning service's own rules — expired or
revoked refresh token families, expired email verification codes, expired or consumed password-reset
tokens, and processed outbox messages past their retention window.

## Architecture

Quartz.NET-scheduled, `CleanupExpiredTokensJob` the one job that exists today. A plain generic `IHost`
like EmailService, no HTTP surface. Runs `[DisallowConcurrentExecution]`, single replica, default
non-clustered `JobStore` — a second replica would run the same job from two pods with no coordination
between them, unlike the outbox dispatcher elsewhere in the system, which uses
`SELECT ... FOR UPDATE SKIP LOCKED` to guard against exactly that class of problem. This job doesn't
need that guard today because it only ever runs as one replica; it would if that changed.

This is a deliberate, named exception to database-per-service
([ADR 0001](../adr/0001-database-per-service.md)): the worker connects to both `identity_db` and
`economy_db` directly through narrow cleanup-only `DbContext`s
(`IdentityCleanupDbContext`/`EconomyCleanupDbContext`), not the services' full models — it deletes,
it never reads either database to serve a request on either service's behalf.

## API

None. No HTTP surface, no health check endpoint — same gap as EmailService, for the same structural
reason (a plain `IHost` has nowhere to expose one).

## Data

Touches, but doesn't own: `identity_db`'s `RefreshTokenFamilies` (cascades to `RefreshTokens` at the
FK level, so that table is never mapped here directly), `EmailVerificationCodes`,
`PasswordResetTokens`; `economy_db`'s `OutboxMessages` (deletes only rows already marked processed,
past a configurable retention window).

## Messaging

None. Doesn't publish or consume — pure scheduled deletion against two databases it doesn't own.

## Dependencies

`identity_db` and `economy_db` directly (Postgres), no other service.

## Security

Nothing publicly reachable. Connection strings to both databases are the only sensitive
configuration, sourced the same way every other service's are (Secrets in Kubernetes, `.env`
locally).

## Deployment

Docker image, `replicas: 1` with **no HPA object at all** — not one capped at `maxReplicas: 1` (that
would still let a scale event fire), the manifest is simply absent, since Quartz's non-clustered
`JobStore` here has no coordination story for a second replica. See
[Backend deployment topology](../architecture/backend.md).

## Observability

OpenTelemetry via `BuildingBlocks.Telemetry`, Serilog to Console + Loki. Each run logs a single
structured summary line (families/codes/tokens/outbox rows deleted) — the only application-level
signal this service currently emits, since there's no health endpoint to probe instead.

## Related documentation

- [ADR 0001: Database per service](../adr/0001-database-per-service.md)
- [Data ownership](../architecture/data.md)
- [README's Platform.Worker section](../../README.md#platformworker)
