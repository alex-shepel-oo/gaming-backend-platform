# Services

One page per service or shared library, focused on architecture and engineering decisions rather
than a generated endpoint reference — full API detail lives under [docs/api/](../api/).

## Backend

- [IdentityService](identity.md) — accounts, authentication, per-game authorization, game catalog
- [EconomyService](economy.md) — currencies, balances, conversions
- [ApiGateway](gateway.md) — single entry point, routing, CORS, claim-based route gating
- [EmailService](email-service.md) — transactional email, no HTTP surface
- [Platform.Worker](platform-worker.md) — cross-database cleanup jobs
- [NotificationService](notification-service.md) — live balance pushes over SignalR
- [BuildingBlocks](building-blocks.md) — shared Auth/Messaging/Telemetry libraries

## Frontend

- [player-client](player-client.md) — the public-facing Angular app
- [admin-client](admin-client.md) — the platform/game-admin console

See [Frontend architecture](../architecture/frontend.md) for what both apps share.

## Related documentation

- [Architecture overview](../architecture.md)
- [Data ownership](../architecture/data.md)
- [Architecture decisions](../adr/README.md)
