# IdentityService

## Responsibility

Owns accounts, authentication, per-game authorization, and the game catalog itself. One account
per email works across every game on the platform; what a given account can do is scoped per game
through `UserGameRoles`, not through separate per-game accounts.

## Architecture

Minimal APIs, route groups per concern (`AuthEndpoints`, `UserEndpoints`, `GameEndpoints`,
`RolePermissionEndpoints`, `JwksEndpoints`, `HealthEndpoints`). Owns `identity_db` outright — see
[Data ownership](../architecture/data.md) for what that means for the rest of the platform.
`api-gateway` is the only path a browser reaches this service through; nothing here trusts a caller
that skipped the gateway's own claim checks, since the gateway's policy is UX-layer defense in depth,
not the actual boundary (see [Security overview](../security/overview.md)).

## API

Full endpoint reference: [Identity API](../api/identity.md). Route groups, at a glance:
authentication (`/auth/*` — register, login, refresh, email confirmation, password reset, JWKS
publishing), profile (`/users/me`), platform/game administration (`/admin/identity/*` — games CRUD
and hard-delete, role/permission management, user roster and role assignment).

## Data

Owns `identity_db`: `Users`, `Games`, `UserGameRoles`, `RolePermissions`, `RefreshTokenFamilies`/
`RefreshTokens`, `RevokedAccessTokens`, `EmailVerificationCodes`, `PasswordResetTokens`,
`ExternalLogins`. See [Data ownership](../architecture/data.md) for the cross-service boundary this
database sits behind.

## Messaging

Publishes to its own outbox on the shared event bus ([ADR 0010](../adr/0010-transactional-outbox-event-bus.md)):
`UserEmailConfirmedEvent` (EconomyService's welcome grant reacts to this), plus the events
EmailService consumes to send verification codes, password-reset links, and duplicate-registration
notices. Consumes nothing itself — it's a source service for these flows, not a sink.

## Dependencies

`api-gateway` in front, RabbitMQ for outbound events, `identity_db` (Postgres). No synchronous
dependency on any other service — a request into IdentityService never blocks on EconomyService or
EmailService being up.

## Security

JWT access tokens (RS256, keys published via its own `/.well-known/jwks.json`,
[ADR 0017](../adr/0017-rs256-and-jwks.md)), refresh tokens rotating through single-use families with
reuse detection ([ADR 0008](../adr/0008-token-strategy.md)), BCrypt password hashing, permission-based
RBAC scoped per `GameId` ([ADR 0013](../adr/0013-permission-based-rbac-and-audience-scoped-tokens.md)).
Rate limiting on every auth-adjacent endpoint (`login`, `register`, `confirm-email`,
`resend-verification`, `request-password-reset`, `reset-password`) — in-process IP limits as a first
line, backed by a database-level per-account cooldown on resend that stays authoritative across
replicas. Full detail: [Security overview](../security/overview.md).

## Deployment

Docker image + `identity-migrator` one-shot migration container/Job. StatefulSet-backed `identity-db`
locally and in Kubernetes (dev/sandbox only — production targets a managed instance). See
[Backend deployment topology](../architecture/backend.md).

## Observability

OpenTelemetry traces/metrics via `BuildingBlocks.Telemetry`, Serilog to Console + Loki, `/health` and
`/health/ready` endpoints. See [Observability overview](../observability/overview.md).

## Related documentation

- [Identity API reference](../api/identity.md)
- [Data ownership](../architecture/data.md)
- [ADR 0005: Multi-tenancy via GameId](../adr/0005-multi-tenancy-gameid.md)
- [ADR 0008: Token strategy](../adr/0008-token-strategy.md)
- [ADR 0013: Permission-based RBAC](../adr/0013-permission-based-rbac-and-audience-scoped-tokens.md)
- [ADR 0017: RS256 + JWKS](../adr/0017-rs256-and-jwks.md)
- [ADR 0025: Close self-service avatar URLs](../adr/0025-close-self-service-avatar-url.md)
- [ADR 0026: Game hard-delete](../adr/0026-game-hard-delete-orphaned-economy-data.md)
