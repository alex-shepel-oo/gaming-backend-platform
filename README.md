# Gaming Backend Platform

A multi-tenant backend platform for games: one shared set of services — identity, economy,
notifications — that several independent games plug into via SDK, each with its own currency,
progression, and rules, without re-implementing auth, wallets, or admin tooling per game. Built
as a portfolio project end to end, infrastructure included: Kubernetes, GitOps deployment,
distributed tracing, and structured logging, running for real on a public VPS rather than only
on `localhost`.

[![identity-ci](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/identity-ci.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/identity-ci.yml)
[![gateway-ci](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/gateway-ci.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/gateway-ci.yml)
[![economy-ci](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/economy-ci.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/economy-ci.yml)
[![platform-worker-ci](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/platform-worker-ci.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/platform-worker-ci.yml)
[![notification-ci](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/notification-ci.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/notification-ci.yml)
[![email-service-ci](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/email-service-ci.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/email-service-ci.yml)
[![player-client-ci](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/player-client-ci.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/player-client-ci.yml)
[![admin-client-ci](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/admin-client-ci.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/admin-client-ci.yml)
[![k8s-validate](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/k8s-validate.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/k8s-validate.yml)
[![gitleaks](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/gitleaks.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/gitleaks.yml)

## Contents

- [Tech stack](#tech-stack)
- [Live demo](#live-demo)
- [Architecture](#architecture)
- [GitOps & CI/CD](#gitops--cicd)
- [Running locally](#running-locally)
- [Running on Kubernetes](#running-on-kubernetes)
- [Identity API](#identity-api)
- [Economy API](#economy-api)
- [NotificationService](#notificationservice)
- [Player-client (Angular)](#player-client-angular)
- [Admin-client (Angular)](#admin-client-angular)
- [Messaging](#messaging)
- [Platform.Worker](#platformworker)
- [Architecture decisions](#architecture-decisions)
- [Known limitations / what's next](#known-limitations--whats-next)

## Tech stack

| | |
|---|---|
| **Backend** | ![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet) ![ASP.NET Core](https://img.shields.io/badge/ASP.NET%20Core-Minimal%20APIs-512BD4?logo=dotnet) ![Ocelot](https://img.shields.io/badge/Ocelot-API%20Gateway-008080) |
| **Frontend** | ![Angular](https://img.shields.io/badge/Angular-20-DD0031?logo=angular) |
| **Data & messaging** | ![PostgreSQL](https://img.shields.io/badge/PostgreSQL-17-336791?logo=postgresql) ![RabbitMQ](https://img.shields.io/badge/RabbitMQ-FF6600?logo=rabbitmq) |
| **Observability** | ![OpenTelemetry](https://img.shields.io/badge/OpenTelemetry-425CC7?logo=opentelemetry&logoColor=white) ![Prometheus](https://img.shields.io/badge/Prometheus-E6522C?logo=prometheus&logoColor=white) ![Grafana](https://img.shields.io/badge/Grafana-F46800?logo=grafana&logoColor=white) ![Loki](https://img.shields.io/badge/Loki-Log%20Aggregation-F46800?logo=grafana&logoColor=white) ![Tempo](https://img.shields.io/badge/Tempo-Distributed%20Tracing-F46800?logo=grafana&logoColor=white) ![Grafana Faro](https://img.shields.io/badge/Faro-Frontend%20Tracing-F46800?logo=grafana&logoColor=white) |
| **Infrastructure** | ![Docker](https://img.shields.io/badge/Docker-2496ED?logo=docker) ![Kubernetes](https://img.shields.io/badge/Kubernetes-326CE5?logo=kubernetes) ![Helm](https://img.shields.io/badge/Helm-0F1689?logo=helm) ![Argo CD](https://img.shields.io/badge/Argo%20CD-EF7B4D?logo=argo) ![Traefik](https://img.shields.io/badge/Traefik-24A1C1?logo=traefikproxy&logoColor=white) |

## Live demo

Running on a real VPS behind Cloudflare, not just locally.

| | |
|---|---|
| [shepel.dev](https://shepel.dev) | Welcome page — links to everything below, plus a short bio |
| [gbplatform.shepel.dev](https://gbplatform.shepel.dev) | Player-facing app — register a real account (email confirmation goes through a real SMTP relay) or explore as a guest: `xosime2935@copawoke.com` / `GuestUser123` |
| [gbgrafana.shepel.dev](https://gbgrafana.shepel.dev) | Observability — dashboards, traces, logs, including [live node/pod resource usage](infra/grafana/dashboards/node-resources.json) and, on the main dashboard, a query that finds real cross-service traces crossing RabbitMQ. Guest login: `viewer` / `GbpDemo2026Viewer!` (read-only) |

`gbargocd.shepel.dev` runs the real GitOps deployment behind this demo (see [GitOps & CI/CD](#gitops--cicd) below) but
isn't linked with a shared login: Argo CD only enforces RBAC on write actions for
cluster/repository-level resources, so any read-only role — anonymous or a named viewer account
alike — ends up able to see cluster endpoints, repo URLs and SSH known-hosts fingerprints
regardless of policy. `gbadmin.shepel.dev` (the admin panel) similarly isn't linked with a shared
login, since it has no built-in read-only guest mode of its own and a public account would mean
real write access to live demo data, not just a view of it.

<details>
<summary>Screenshots</summary>

<details>
<summary>Player Client</summary>

### Home

Public landing page: the platform's pitch (shared identity, one wallet, currencies that convert
across games), a feature highlight grid, and a roadmap section for what's next.

 <img src="docs/screenshots/player/pc/player-home-PC.png" width="700">

 <details>
 <summary>Mobile</summary>
 <img src="docs/screenshots/player/mobile/player-home-mobile.png" width="250">
 </details>

### Login

Authentication screen for the player client.

<img src="docs/screenshots/player/gifs/player-login-register.gif" width="700">

 <details>
 <summary>Mobile</summary>
 <img src="docs/screenshots/player/gifs/player-login-register-mobile.gif" width="250">
 </details>

 ### Reset password

Reset password screnns with expire link screen for the player client.

<img src="docs/screenshots/player/gifs/player-reset-pass.gif" width="700">

 <details>
 <summary>Mobile</summary>
 <img src="docs/screenshots/player/gifs/player-reset-pass-mobile.gif" width="250">
 </details>


### Wallet

Real-time wallet balance updates delivered through SignalR.

<img src="docs/screenshots/player/gifs/player-wallet.gif" width="700">

 <details>
 <summary>Mobile</summary>
 <img src="docs/screenshots/player/gifs/player-wallet-mobile.gif" width="250">
 </details>

### Convert

Converting platform credits to a game currency, with the resulting balance update delivered through SignalR.

<img src="docs/screenshots/player/gifs/player-convert.gif" width="700">

 <details>
 <summary>Mobile</summary>
 <img src="docs/screenshots/player/gifs/player-convert-mobile.gif" width="250">
 </details>

### Games

Game catalog and detail view.

<img src="docs/screenshots/player/pc/player-games-PC.png" width="700">

 <details>
 <summary>Mobile</summary>
 <img src="docs/screenshots/player/gifs/player-games-mobile.gif" width="250">
 </details>

### Profile

Player profile — account details, member-since date, and avatar.

<img src="docs/screenshots/player/pc/player-account-PC.png" width="700">

 <details>
 <summary>Mobile</summary>
 <img src="docs/screenshots/player/mobile/player-account-mobile.png" width="250">
 </details>

</details>

<details>
<summary>Emails</summary>

### Register code

Verification code sent after registering, needed to confirm the account before it can log in.

<img src="docs/screenshots/email/email-code.png" width="700">

### Reset password

Password reset link sent after requesting one from the login screen, expires after a fixed window.

<img src="docs/screenshots/email/email-reset-pass.png" width="700">

### Duplicate registration attempt

Sent instead of a new verification code when someone tries to register an email that already has
an account for that game; the existing account is left untouched.

<img src="docs/screenshots/email/email-attempt.png" width="700">

</details>

<details>
<summary>Admin Client</summary>

### Login

Authentication screen for the admin client.

<img src="docs/screenshots/admin/pc/admin-login-PC.png" width="700">

 <details>
 <summary>Mobile</summary>
 <img src="docs/screenshots/admin/mobile/admin-login-mobile.png" width="250">
 </details>

### Game Edits

Editing a game's metadata as a platform admin.

<img src="docs/screenshots/admin/gifs/admin-games-edit.gif" width="700">

 <details>
 <summary>Mobile</summary>
 <img src="docs/screenshots/admin/gifs/admin-games-edit-mobile.gif" width="250">
 </details>

### Users

Searching users and viewing their account details.

<img src="docs/screenshots/admin/gifs/admin-players-edit.gif" width="700">

 <details>
 <summary>Mobile</summary>
 <img src="docs/screenshots/admin/gifs/admin-players-edit-mobile.gif" width="250">
 </details>

</details>

<details>
<summary>Observability</summary>

### Dashboard - main board

Service overview dashboard under real traffic.

<img src="docs/screenshots/grafana/grafana-dash.png" width="700">

### Dashboard - node & pod resources

Node & pod resources dashboard under real traffic.

<img src="docs/screenshots/grafana/gifs/grafana-node-pod_resources.gif" width="700">

### Trace

A real registration trace in Explore, crossing `IdentityService → RabbitMQ → EconomyService` through the welcome-grant flow.

<img src="docs/screenshots/grafana/grafana-trace.png" width="700">

### Tracking by user ID

Filtering traces to everything a specific player triggered, by their `enduser.id`.

<img src="docs/screenshots/grafana/grafana-by-userID.png" width="700">

</details>

<details>
<summary>Deployment</summary>

Argo CD's resource tree after a real sync — every object this chart manages, healthy.

<img src="docs/screenshots/argocd/gifs/argocd-showcase.gif" width="700">

</details>

<details>
<summary>API</summary>

Interactive OpenAPI reference for IdentityService, served through the gateway.

<img src="docs/screenshots/scalar/scala-api.png" width="700">

</details>

</details>

> **Status:** Slice 3's backend is complete and running live in production on Kubernetes via Argo
> CD. Next up: inventory (slice 3b) and continued polish on the pieces above.

## Architecture

```mermaid
flowchart LR
    Player[player-client] --> GW[ApiGateway]
    Admin[admin-client] --> GW

    GW --> ID[IdentityService]
    GW --> EC[EconomyService]
    GW -.-> NS[NotificationService]

    ID -->|user.email_confirmed| MQ[(RabbitMQ)]
    MQ -->|welcome grant| EC
    EC -->|balance.changed| MQ
    MQ -.->|SignalR push| NS
    ID -->|email_verification.requested| MQ
    MQ --> ES[EmailService]

    ID --> IDDB[(identity_db)]
    EC --> ECDB[(economy_db)]
    W[Platform.Worker] --> IDDB
    W --> ECDB
```

Every service also pushes OTLP traces and metrics to an otel-collector (not drawn above, to keep
the request-flow diagram readable) — see [GitOps & CI/CD](#gitops--cicd) below for how a change
actually reaches this diagram in production, and [docs/architecture.md](docs/architecture.md) for
the full breakdown, including local-vs-Kubernetes differences and every implemented-vs-planned
distinction.

Deeper reading, one page per concern rather than one long scroll:

- **[Frontend architecture](docs/architecture/frontend.md)** — both Angular apps' real structure,
  the actual browser-to-backend request path, and the auth lifecycle
- **[Data ownership](docs/architecture/data.md)** — who owns what across `identity_db`/`economy_db`,
  and what crossing that boundary costs
- **[Business/technical flows](docs/architecture/flows.md)** — end-to-end sequence diagrams:
  registration, login/refresh, the conversion saga, game hard-delete's consistency gap, CI→GitOps
- **[Security overview](docs/security/overview.md)** — what's actually enforced, and what isn't
- **[Observability overview](docs/observability/overview.md)** — the OpenTelemetry/Grafana stack,
  walked through on one real trace

## GitOps & CI/CD

CI is per-service and path-filtered (nine workflows, each building/testing/scanning only the service
its trigger paths cover); a passing build ends with that service's own workflow bumping its
`imageTag` file, the commit Argo CD's `main`-watching sync actually reacts to.

[Read GitOps and CI/CD →](docs/operations/gitops.md)

## Running locally

```
scripts/all/deploy.sh
```

Brings up the whole compose stack — both databases, Consul, RabbitMQ, Mailpit, every backend
service, and both frontend apps — from a fresh `.env` generated on first run.

[Read local development →](docs/operations/local-development.md)

## Running on Kubernetes

One Helm chart, one namespace, deployed the same way locally (`kind`) and in production. Secrets are
either applied directly (disposable local values) or SOPS/`age`-encrypted and committed
(production-shaped values meant to survive a rebuild).

[Read deployment →](docs/operations/deployment.md)

## Identity API

Auth, sessions, profile, and the platform's tenant registry, plus the permission-based RBAC model
every role runs on: a role is a named, editable set of permissions resolved fresh at login/refresh
time, not a fixed enum ([ADR 0013](docs/adr/0013-permission-based-rbac-and-audience-scoped-tokens.md)),
with cookie-based refresh for browser clients ([ADR 0011](docs/adr/0011-web-auth-cookie-flow.md)),
RS256 + JWKS signing ([ADR 0017](docs/adr/0017-rs256-and-jwks.md)), and an admin surface gated on a
separate token audience ([ADR 0016](docs/adr/0016-admin-surface-isolation.md)). Full endpoint
reference, both walkthroughs, and the RBAC catalog/claims/anti-escalation details:
**[docs/api/identity.md](docs/api/identity.md)**.

## Economy API

Wallets, ledger transactions, and platform-to-game currency conversion, direct at
`http://localhost:5001` and partly proxied through the gateway. Runs on NUnit rather than
IdentityService's xUnit, on purpose, to show working knowledge of both. Full endpoint reference,
the reasoning behind returning `402` for insufficient funds, and the conversion saga's
compare-and-swap design: **[docs/api/economy.md](docs/api/economy.md)**.

## NotificationService

Turns `BalanceChanged` events into a live SignalR push to whichever browser tab is connected —
port 5003, no database, no backplane (single replica is this slice's accepted scale). The hub
isn't reachable through Ocelot (a measured WebSocket-upgrade failure, not an assumption), so each
frontend's own Nginx proxies `/hubs` directly. Full endpoint reference and the `IUserIdProvider`
gap that would otherwise make deliveries silently vanish: **[docs/services/notification-service.md](docs/services/notification-service.md)**.

## Player-client (Angular)

The platform's first browser client — Login, Games, Wallet, Convert, Profile, password reset —
built on Angular 22 under `frontend/`. Keeps the access token in memory only, relies on the
`gbp_refresh` httpOnly cookie for silent refresh, and treats route guards as UX rather than a
security boundary ([ADR 0012](docs/adr/0012-frontend-security-and-guards.md)). Build/run commands,
the cookie flow in detail, and the CORS setup: **[docs/services/player-client.md](docs/services/player-client.md)**.

## Admin-client (Angular)

A second Angular app, same `shared` library, covering platform and game admin/moderator tooling on
its own audience-gated surface ([ADR 0016](docs/adr/0016-admin-surface-isolation.md)) — one app,
not two, with platform-wide and game-scoped sections both gated by permission. Build/run commands,
the game-picker login flow, and the screen-by-screen breakdown:
**[docs/services/admin-client.md](docs/services/admin-client.md)**.

## Messaging

The transactional outbox/inbox mechanism every publisher and consumer in the system shares
(`BuildingBlocks.Messaging`) — at-least-once delivery, topic-exchange topology, inbox-lite
deduplication, and the welcome-grant flow that made it the first cross-service event binding in
the platform. Full flow, delivery guarantees, and known limitations: **[docs/messaging.md](docs/messaging.md)**.

## Platform.Worker

A separate Quartz-scheduled project for operational housekeeping, rather
than a timer bolted onto each service. It runs one job today,
`CleanupExpiredTokensJob`, on a 15-minute schedule:

- **identity_db:** deletes expired or already-revoked `refresh_token_families`
  (which cascades to their `refresh_tokens` at the database FK level),
  expired `email_verification_codes`, and expired or already-consumed
  `password_reset_tokens`.
- **economy_db:** deletes `outbox_messages` rows that have been dispatched
  (`processed_at` set) and are older than a 7-day retention window. Rows
  still waiting to be dispatched (`processed_at IS NULL`) are never touched.

Both thresholds are configurable (`CleanupJob__IntervalMinutes`,
`CleanupJob__OutboxRetentionDays` in `infra/.env`).

**Why one worker reads two databases.** This is the one place in the system
that opens connections to both `identity_db` and `economy_db` at once, which
is a named exception to [ADR 0001](docs/adr/0001-database-per-service.md)'s
database-per-service boundary, not an oversight of it. The distinction the
exception rests on: this job never reads either database to serve a
request, only to delete rows each owning service already considers dead, by
that service's own rules. It does so through narrow, cleanup-only
`DbContext`s scoped to just the columns it deletes by, not the full
`IdentityDbContext`/`EconomyDbContext` models.

## Architecture decisions
[docs/adr/](docs/adr/).

## Known limitations / what's next

- Gateway routing tests (a WireMock stub standing in for IdentityService,
  asserting the gateway itself rejects unauthenticated requests before they
  reach a downstream service) were left out of this slice; `OcelotConfigurationTests`
  covers the static configuration instead. This is the honest gap rather than
  a broken or skipped test.
  - Access token revocation has a bounded window: revoking a session via
  `revoke-sessions` does not deny-list the access tokens already issued
  under it, since their `jti`s were never recorded at issue time. A revoked
  session's access token stays valid for up to its own 15-minute lifetime.
  See ADR 0008.
- A permission change to `role_permissions` isn't instant: the caller keeps
  the `perms` an already-issued access token carries until their next
  refresh (bounded by that token's own lifetime, ≤15 minutes). Forcing an
  immediate cutoff still means `revoke-sessions`, same as above but for a
  different reason - one caps how long a *revoked* session's token stays
  valid, this one caps how stale a *still-valid* session's permissions can
  get. See [ADR 0013](docs/adr/0013-permission-based-rbac-and-audience-scoped-tokens.md).
- The game registry (`games` table) lives inside IdentityService's own
  database. It conceptually belongs to a platform-level service, which
  does not exist yet at this stage of the build. See ADR 0005.
- No refresh grace window: a client that loses the network response to a
  legitimate `/refresh` call and retries with the same (now-consumed)
  token is treated as reuse and loses the whole session, not just that
  request. See ADR 0008.
- `external_logins` is schema only — no OAuth provider (Google, Discord, etc.)
  is actually wired up yet, and there's no account-linking policy implemented
  either. See ADR 0015.
- Rate limits on login/register/confirm/resend are enforced per gateway
  replica, not per cluster — the deployment scales to ten replicas, so the
  effective budget is the configured limit times whichever replica count
  is currently running. The per-account cooldown on resend is the
  exception: it is enforced in the database and holds regardless of
  replica count.
- `Platform.Worker`'s cleanup job connects to both `identity_db` and
  `economy_db`, a named exception to [ADR 0001](docs/adr/0001-database-per-service.md)'s
  database-per-service boundary made for housekeeping only — it never reads
  either database to serve a request, only to delete rows each owning
  service already considers dead.
- The web cookie flow needs the browser to see its frontend and the API as
  one origin, which is what lets the refresh cookie use `SameSite=Strict`
  with no CSRF token. That no longer means a single shared origin for the
  whole platform: `admin-client` is a genuinely separate frontend, on its
  own host and port, and still gets a clean `SameSite=Strict` cookie,
  because its own Nginx reverse-proxies `/api` onto itself the same way
  `player-client`'s does — the browser never makes a cross-origin call
  either way. See ADR 0011 and
  [ADR 0016](docs/adr/0016-admin-surface-isolation.md).
- A true backend-for-frontend — one where the server itself holds the
  tokens, not just a reverse proxy that keeps the browser same-origin with
  its own frontend — would keep even more token handling out of the browser
  than the cookie-only approach both clients use today. `admin-client`'s own
  origin doesn't do this: the access token still lives in browser memory and
  the refresh cookie still goes straight through the proxy to
  IdentityService, so that gap is unchanged, just now shared by two
  frontends instead of one.
- The conversion saga is in-process and sequential, not cross-service
  choreography over the message bus — a second service reacting to an
  event mid-saga needs InventoryService, which doesn't exist until slice 3.
  See [ADR 0010's addendum](docs/adr/0010-transactional-outbox-event-bus.md#addendum-the-conversion-saga).
- The deduplicating consumer's inbox is inbox-lite: `message_id` and
  `processed_at`, nothing more. No per-message retry bookkeeping or
  metadata — that's the full inbox pattern, still Extended scope.
- Kubernetes Secrets are plain `Secret` objects applied from the
  `secrets.example/*.yaml` templates, not sourced from an external secrets
  manager, and deliberately kept outside the Helm release itself so an
  `upgrade` never touches them. Something like Sealed Secrets came up as the
  natural next step here and wasn't built in this group.
- RabbitMQ and both Postgres instances run as dev/sandbox-only images
  (`rabbitmq:4-management-alpine`, `postgres:17-alpine`) in every environment
  this repo currently deploys to. A production target would point at managed
  instances of both instead.