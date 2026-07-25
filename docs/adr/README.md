# Architecture Decision Records

Each architectural decision is documented on a single page, following the format:
**Context → Decision → Trade-offs.**

See the template: [0000-template.md](0000-template.md).

| # | Decision | Status | Milestone |
|---|---|---|---|
| [0001](0001-database-per-service.md) | Database per Service | Accepted | 1 |
| [0002](0002-api-gateway-ocelot-consul.md) | API Gateway: Ocelot + Consul | Accepted | 1 |
| [0003](0003-async-inter-service-communication.md) | Synchronous vs. Asynchronous Inter-Service Communication | Accepted | 2 |
| 0004 | Rule Engine: RulesEngine Now, Jint Sandbox Next | Planned | — |
| [0005](0005-multi-tenancy-gameid.md) | Multi-Tenancy via `GameId` | Accepted | 1 |
| 0006 | CI/CD Environments: Local / CI Ephemeral / Azure | Planned | — |
| [0007](0007-minimal-apis-over-controllers.md) | Minimal APIs over Controllers | Accepted | 1 |
| [0008](0008-token-strategy.md) | Token Strategy: Short-Lived Access Tokens + Rotating Refresh Token Families | Accepted | 1 |
| [0009](0009-email-confirmation-flow.md) | Email Confirmation Flow | Accepted | 1 |
| [0010](0010-transactional-outbox-event-bus.md) | Messaging: Manual Outbox and Conversion Saga behind an `IEventBus` Abstraction | Accepted | 2 |
| [0011](0011-web-auth-cookie-flow.md) | Web Auth: Cookie-Based Refresh Flow | Accepted | 2 |
| [0012](0012-frontend-security-and-guards.md) | Frontend Security and Route Guards | Accepted | 2 |
| [0013](0013-permission-based-rbac-and-audience-scoped-tokens.md) | Permission-Based RBAC, Audience-Scoped Tokens, and Ecosystem-First Scope | Accepted | 3 |
| [0014](0014-notification-service-and-signalr.md) | NotificationService and Real-Time Delivery over SignalR | Accepted | 3 |
| 0015 | Auth-Cluster Hardening: Password Reset, Anti-Enumeration, OAuth Groundwork | Planned | 3 |
| 0016 | Admin Surface Isolation | Planned | 3 |
| 0017 | RS256 + JWKS | Planned | 3 |
| [0018](0018-shared-messaging-building-block.md) | Shared Messaging Building Block (`BuildingBlocks.Messaging`) | Accepted | 3 |