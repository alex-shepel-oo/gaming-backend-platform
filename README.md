# Gaming Backend Platform

This is a portfolio project designed to demonstrate practical experience in building modern backend systems using a microservices architecture. It showcases backend development, frontend integration, infrastructure design, deployment, and the operation of distributed applications in a production-like environment.

The project implements a multi-tenant backend platform for games. Each game has its own economy, inventory, progression, and validation rules while leveraging a shared set of backend services and infrastructure. Games integrate through an SDK, enabling them to reuse common functionality while remaining logically isolated.

> **Status:** Slice 3 in progress — permission-based RBAC, admin panel, NotificationService (SignalR), auth improvements (3a); InventoryService + cross-service choreography saga (3b)

[![identity-ci](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/identity-ci.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/identity-ci.yml)
[![gateway-ci](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/gateway-ci.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/gateway-ci.yml)
[![economy-ci](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/economy-ci.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/economy-ci.yml)
[![platform-worker-ci](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/platform-worker-ci.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/platform-worker-ci.yml)
[![player-client-ci](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/player-client-ci.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/player-client-ci.yml)
[![k8s-validate](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/k8s-validate.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/k8s-validate.yml)
[![gitleaks](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/gitleaks.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/gitleaks.yml)

## Tech stack
![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET%20Core-Minimal%20APIs-512BD4?logo=dotnet)
![Angular](https://img.shields.io/badge/Angular-20-DD0031?logo=angular)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-17-336791?logo=postgresql)
![RabbitMQ](https://img.shields.io/badge/RabbitMQ-FF6600?logo=rabbitmq)
![Ocelot](https://img.shields.io/badge/Ocelot-API%20Gateway-008080)
![Docker](https://img.shields.io/badge/Docker-2496ED?logo=docker)
![Kubernetes](https://img.shields.io/badge/Kubernetes-326CE5?logo=kubernetes)

## CI

| Workflow | Triggers on | Checks |
|---|---|---|
| [identity-ci](.github/workflows/identity-ci.yml) | `backend/IdentityService/**`, `backend/IdentityService.Tests/**`, `backend/Directory.*.props`, `global.json` | `dotnet build` + `dotnet test`, then pushes `identity-service` to GHCR |
| [gateway-ci](.github/workflows/gateway-ci.yml) | `backend/ApiGateway/**`, `backend/ApiGateway.Tests/**`, `backend/Directory.*.props`, `global.json` | `dotnet build` + `dotnet test`, then pushes `api-gateway` to GHCR |
| [economy-ci](.github/workflows/economy-ci.yml) | `backend/EconomyService/**`, `backend/EconomyService.Tests/**`, `backend/Directory.*.props`, `global.json` | `dotnet build` + `dotnet test`, Trivy filesystem scan, pushes `economy-service` to GHCR, then Trivy image scan |
| [platform-worker-ci](.github/workflows/platform-worker-ci.yml) | `backend/Platform.Worker/**`, `backend/Platform.Worker.Tests/**`, `backend/Directory.*.props`, `global.json` | same shape as `economy-ci`, scoped to `platform-worker` |
| [player-client-ci](.github/workflows/player-client-ci.yml) | `frontend/**` | Node 22, `npm ci` + `npm run build` + `npm run test` (Vitest), Trivy filesystem scan, pushes `player-client` to GHCR, then Trivy image scan |
| [k8s-validate](.github/workflows/k8s-validate.yml) | `infra/kubernetes/**` | renders the Kustomize tree and validates it with `kubeconform` |
| [gitleaks](.github/workflows/gitleaks.yml) | every push/PR to `main`/`develop`, whole repository | scans the full git history (not just the diff) for committed secrets |

Path filters mean touching `backend/EconomyService/` doesn't trigger
`identity-ci`, and vice versa — each service only rebuilds and retests on the
changes that could actually affect it.

**Gitleaks results don't go to the Security tab.** It's a job-summary/PR-comment
report instead, on purpose: gitleaks scans the whole git history, so a hit
means the secret is sitting in some past commit, not just the working tree. A
Security tab alert reads as "fix the code, dismiss the alert" — the actual fix
here is rotating the leaked credential, which no code change accomplishes, so
routing it through the same UI as a code-scanning finding would be misleading.

**Trivy results do go to the Security tab** — both the dependency scan and the
image scan upload SARIF via `github/codeql-action/upload-sarif`. There's no
standalone `trivy` badge above because Trivy isn't its own workflow: it runs
as a step inside `economy-ci`, `platform-worker-ci` and `player-client-ci`
(`.github/actions/trivy-scan`), right after each one pushes its image, so
what gets scanned is the image that would actually reach the cluster.

## Architecture
[docs/architecture.md](docs/architecture.md).

## Running locally

```
cp infra/.env.example infra/.env
cd infra
docker compose up
```

This brings up the whole stack in one command: both Postgres instances,
Consul, RabbitMQ, Mailpit, IdentityService, EconomyService, Platform.Worker,
ApiGateway and player-client. The browser client is at
`http://localhost:8080`; anything hitting the API directly goes through the
gateway at `http://localhost:5100`. Mailpit's UI (for reading verification
emails without a real mailbox) is at `http://localhost:8025`.

The values in `infra/.env.example` are committed on purpose and are not
production secrets: the stack only binds to `localhost`, so nothing in it is
reachable from outside the machine it runs on, and every clone gets its own
`.env` by copying the example rather than sharing one committed file.

## Running on Kubernetes

Manifests live under `infra/kubernetes/` — `base/`, `identity/`, `economy/`,
`rabbitmq/`, `worker/`, `player-client/`, `gateway/`, `mailpit/` — one
namespace (`gaming-platform`) with the same services as the compose stack
above. They target a local `kind` cluster or a sandbox namespace, not
production — see [docs/architecture.md](docs/architecture.md#local-vs-kubernetes)
for the full local-vs-cluster breakdown, including why the environment is
pinned to `Development` here.

Secrets are never committed; each service ships a `secret.example.yaml`
template instead:

```
cp infra/kubernetes/identity/secret.example.yaml /tmp/identity-secrets.yaml
cp infra/kubernetes/economy/secret.example.yaml /tmp/economy-secrets.yaml
cp infra/kubernetes/rabbitmq/secret.example.yaml /tmp/rabbitmq-secrets.yaml
# edit each of the three with real values, then:
kubectl apply -f /tmp/identity-secrets.yaml -f /tmp/economy-secrets.yaml -f /tmp/rabbitmq-secrets.yaml
scripts/k8s/apply.sh
```

`gateway` and `economy-service` both read the JWT signing key out of
`identity-secrets` rather than holding a copy of their own, and Consul is not
deployed at all here — Kubernetes Services and kube-DNS already provide
discovery (ADR 0002).

`scripts/k8s/apply.sh` (no argument defaults to the whole `infra/kubernetes`
tree) does more than a bare `kubectl apply -f`: the two database
StatefulSets are applied and waited on first, then the `identity-migrator`/
`economy-migrator` Jobs are applied and waited on to completion, and only
after that does the rest of the tree — including `mailpit` and
`player-client` — get applied. That ordering is spelled out explicitly
because a Kubernetes `Job` has no `depends_on: condition:
service_completed_successfully` equivalent the way compose's migrator
containers do.

The `gateway/` part of that same render-and-apply step goes through
Kustomize rather than being folded into a plain file list: its ConfigMap is
generated directly from `backend/ApiGateway/ocelot.Kubernetes.json`, so
there's no hand-copied routing table that can drift from the real one. That
source file lives outside `infra/kubernetes/gateway/`, though, and `kubectl
apply -k` has no flag to let Kustomize read outside the kustomization's own
directory — only the separate `kubectl kustomize` render command does — so
the script renders with `kubectl kustomize
--load-restrictor=LoadRestrictionsNone` first and pipes the result into
`kubectl apply -f -`.

Reach the stack through the Ingress (assumes `ingress-nginx`; its one rule
routes `/` to `player-client`, which proxies `/api` onward to the gateway
itself — see [docs/architecture.md](docs/architecture.md#local-vs-kubernetes)),
or port-forward directly:

```
kubectl -n gaming-platform port-forward svc/player-client 8080:8080
kubectl -n gaming-platform port-forward svc/api-gateway 5100:5100
kubectl -n gaming-platform port-forward svc/mailpit 8025:8025   # kind/sandbox only
```

## Local automation

`scripts/` mirrors the CI steps above for local use — each script calls the
same commands its corresponding workflow does, rather than a parallel set of
commands that could quietly drift from what CI actually checks.

```
scripts/
├── backend/
│   ├── build.sh    # dotnet build backend/GamingBackendPlatform.slnx
│   ├── test.sh     # dotnet test backend/GamingBackendPlatform.slnx
│   └── deploy.sh   # docker compose up -d, every backend service, no player-client
├── frontend/
│   ├── build.sh    # npm ci && npm run build (shared, then player-client)
│   ├── test.sh     # npm run test (Vitest, both projects)
│   └── deploy.sh   # docker compose up -d player-client
├── all/
│   ├── verify.sh   # backend build+test, then frontend build+test -- no deploy
│   ├── deploy.sh   # docker compose up -d, the whole stack
│   ├── ci.sh       # verify.sh, then deploy.sh
│   └── stop.sh     # docker compose down (--clean also drops volumes and prunes images)
└── k8s/
    ├── apply.sh     # render infra/kubernetes with Kustomize and apply it (see above)
    └── teardown.sh  # kind delete cluster --name gbp
```

**`verify.sh` vs `ci.sh`:** `verify.sh` is the quick local gate — build and
test both sides and stop there, nothing gets deployed afterward. `ci.sh` is
`verify.sh` plus a full-stack deploy at the end, for when the goal is a
running stack, not just confirmation that everything builds and passes.

## Identity API

All paths below are relative to `http://localhost:5100`. "Auth" is what the
gateway itself enforces; IdentityService applies its own, more granular
policies underneath regardless of what the gateway already checked.

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/api/identity/auth/register` | anonymous | Register an account for a game; 202, email confirmation required |
| POST | `/api/identity/auth/confirm-email` | anonymous | Confirm the code sent by email |
| POST | `/api/identity/auth/resend-verification` | anonymous | Request a new confirmation code |
| POST | `/api/identity/auth/login` | anonymous | Exchange credentials for a token pair (or an access token plus a refresh cookie, in web mode) |
| POST | `/api/identity/auth/refresh` | anonymous | Rotate a refresh token for a new pair (body or cookie, depending on mode) |
| POST | `/api/identity/auth/logout` | anonymous at the gateway, bearer required by the service | Revoke the current session |
| GET | `/api/identity/users/me` | bearer | Current user's profile |
| GET | `/api/identity/users/{userId}` | bearer | Look up a user in the caller's game (moderator and above) |
| GET | `/api/identity/users` | bearer | Search/paginate users in the caller's game (moderator and above) |
| POST | `/api/identity/users/{userId}/revoke-sessions` | bearer | Revoke all of a user's sessions (admin) |
| GET | `/api/identity/games` | bearer, admin role required | List registered games, all fields |
| GET | `/api/identity/games/public` | bearer, any player | List active games only, `id`/`slug`/`name` only - the catalog a player picks a game from |
| GET | `/openapi/identity/v1.json` | anonymous | IdentityService's OpenAPI document, proxied through the gateway |
| GET | `/scalar/identity` | anonymous | Interactive API reference (Scalar) |
| GET | `/health` | anonymous | Gateway liveness probe |

### Web auth (cookie mode)

`login`, `refresh` and `logout` above default to slice 1's body-based contract:
both tokens travel in the JSON body, which is what the Postman collection and
any non-browser client still gets. Sending `X-Client-Type: web` switches a
caller onto the cookie-based flow instead: the response body carries only the
access token, meant to be held in memory, while the refresh token is set as an
`httpOnly` cookie the page's own JavaScript never sees. See
[ADR 0011](docs/adr/0011-web-auth-cookie-flow.md) for the full attribute list
and the SameSite/CORS reasoning.

### Local walkthrough

Registers a player against the seeded `demo-shooter` game, confirms the
account from the email Mailpit caught, logs in, rotates the refresh token,
and shows that a reused (already-rotated) refresh token is rejected.

```bash
# 1. Register
curl -s -X POST http://localhost:5100/api/identity/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"player1@example.com","password":"CorrectHorseBattery9!","displayName":"Player One","gameSlug":"demo-shooter"}'

# 2. Read the verification code Mailpit caught (or open http://localhost:8025)
curl -s "http://localhost:8025/api/v1/search?query=to:player1@example.com" \
  | jq -r '.messages[0].ID' \
  | xargs -I{} curl -s "http://localhost:8025/api/v1/message/{}" \
  | jq -r '.Text'

# 3. Confirm, using the code from step 2
curl -s -X POST http://localhost:5100/api/identity/auth/confirm-email \
  -H "Content-Type: application/json" \
  -d '{"email":"player1@example.com","code":"<code from step 2>"}'

# 4. Log in
curl -s -X POST http://localhost:5100/api/identity/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"player1@example.com","password":"CorrectHorseBattery9!","gameSlug":"demo-shooter"}'

# 5. Refresh (use the refreshToken from step 4)
curl -s -X POST http://localhost:5100/api/identity/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<refreshToken from step 4>"}'

# 6. Reuse the same (now rotated-out) refresh token -- rejected with 401
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5100/api/identity/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<refreshToken from step 4>"}'
```

## Economy API

Reachable directly at `http://localhost:5001`, and also proxied through the
gateway at `http://localhost:5100/api/economy/...` (same paths, `/api/economy`
prefix) for `balances/me`, `transactions/me`, and `conversions` — the routes
player-client actually calls. `currencies`, `balances/{userId}/adjust`,
`transactions/grant`, and `transactions/spend` stay direct-only for now; no
current client goes through the gateway for them.

Currencies come in two scopes: **platform** currencies (`gameId` is `null`,
shared across every game) and **game** currencies (`gameId` set, scoped to one
title). The seeded development data has `PLATFORM_CREDITS` (platform) and
`SHOOTER_GOLD` (game, `demo-shooter`), with a `100:1` conversion rate between
them that nothing consumes yet.

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/currencies` | bearer | Platform currencies plus the caller's own game currency |
| GET | `/balances/me` | bearer | Current user's balances (`?gameId=` cross-checks against the token's own game, it does not select a different one) |
| POST | `/balances/{userId}/adjust` | bearer, admin | Manual correction with a required audit `reason`; `Amount` is a signed delta, not a magnitude |
| POST | `/transactions/grant` | bearer, moderator+ | Credit a user's balance, with an audit `reason` |
| POST | `/transactions/spend` | bearer | Debit the caller's own balance |
| GET | `/transactions/me` | bearer | Paginated ledger history (`?currencyId=&page=&pageSize=`) for the caller only |
| POST | `/conversions` | bearer | Start a platform-to-game currency conversion; `202` with `Started`, not the final outcome |
| GET | `/conversions/{id}` | bearer | Poll a conversion's status; owner-scoped, `404` on someone else's id |
| GET | `/health` | anonymous | Liveness probe |
| GET | `/health/ready` | anonymous | Readiness probe (Postgres and RabbitMQ) |

`grant`, `spend`, and `adjust` all require an `Idempotency-Key` header (400
without one); replaying the same key returns the original outcome instead of
posting twice, keyed off `ledger_entries.idempotency_key`.

### Why 402 for insufficient funds

A `spend` (or a downward `adjust`) that would take a balance below zero
returns `402 Payment Required` rather than `400`/`409`/`422`. It is the one
status code in the 4xx range whose name actually describes "not enough
money," which makes it easier to branch on client-side than yet another
generic conflict/validation code sharing space with unrelated failures.

### Why NUnit here

EconomyService's tests run on NUnit instead of xUnit (which IdentityService
uses), on purpose, to show working knowledge of both. NSubstitute,
AwesomeAssertions, and Testcontainers are the same across both projects
either way.

### Conversion saga

Converting platform currency to a game's own currency is a two-step
operation with a compensating rollback: `POST /conversions` returns `202`
with status `Started` right away, and the client polls `GET
/conversions/{id}` for the outcome. A background runner picks the request up
off an in-process channel, debits the platform balance, then credits the
game balance; if the credit step fails, a compensating entry restores the
debited amount and the request lands in `Failed` instead of `Completed`.

This is an in-process, sequential saga, not choreography over RabbitMQ -
both currencies belong to EconomyService, so there is no second service to
react to an event between the two steps. Each transition commits and is
recorded on `conversion_requests.status`, so a crash mid-saga leaves a
readable state rather than an ambiguous one. See [ADR 0010's
addendum](docs/adr/0010-transactional-outbox-event-bus.md#addendum-the-conversion-saga)
for the full reasoning, including why this isn't genuine cross-service
choreography (that needs InventoryService, which doesn't exist until slice
3).

## Player-client (Angular)

An Angular 22 workspace under `frontend/` (`shared` library + `player-client`
app) — the first browser client for the platform, covering Login, Games,
Wallet, and Convert.

### Running it

Built image (matches the demo path, proxies `/api` through Nginx):

```
cd frontend
docker build -f projects/player-client/Dockerfile -t player-client .
docker run -p 8080:8080 --network infra_platform-network player-client
```

Reach it at `http://localhost:8080`. It isn't wired into
`infra/docker-compose.yml` yet — that's Kubernetes/compose plumbing for a
later group, not part of this one.

Local iteration:

```
cd frontend
npm install
npm run build   # shared first, then player-client -- player-client's
                 # tsconfig resolves "shared" against shared's built dist,
                 # not its source
npm start        # ng serve, http://localhost:4200
npm test         # Vitest, both projects
```

`ng serve` doesn't go through the Nginx proxy, so it relies on the gateway's
CORS whitelist (below) rather than a same-origin `/api` path. It currently
has no `proxy.conf.json`, so API calls resolve against `:4200` itself unless
one is added — see known limitations.

### Cookie flow, client side

The access token lives only in an in-memory signal — never localStorage,
sessionStorage, or a cookie the client can read. The refresh token is the
`gbp_refresh` `httpOnly` cookie IdentityService sets (see
[ADR 0011](docs/adr/0011-web-auth-cookie-flow.md)); the client never touches
its value, just relies on `withCredentials: true` sending it automatically.
An HTTP interceptor attaches the access token and retries once on 401 after
a refresh; a silent refresh call at bootstrap restores the session on
reload, before routing decides anything. Route guards are UX only, not the
security boundary — see [ADR 0012](docs/adr/0012-frontend-security-and-guards.md)
for the full reasoning.

### CORS

The gateway whitelists `http://localhost:8080` and `http://localhost:4200`
(`Cors:AllowedOrigins`, `AllowCredentials=true`, `X-Client-Type` in
`AllowedHeaders`) via `AddCors`/`UseCors` middleware ahead of `UseOcelot()`,
not Ocelot's own per-route CORS config. The built demo path doesn't need it
at all, since Nginx proxies `/api` onto the same origin as the static files
— CORS only matters for `ng serve` and any future direct client.

### Known limitations

- **Admin panel not built.** Slice 3 scope.
- **`ng serve` has no `proxy.conf.json` yet.** Local dev currently needs
  either that file (pointing `/api` at the gateway) or manually hitting the
  gateway's absolute URL; CORS alone doesn't help until requests are
  actually cross-origin.
- **Login is hardcoded to one game** (`DEFAULT_GAME_SLUG = 'demo-shooter'`,
  see the games screen commit). There's no game picker.
- **"Log into the ecosystem, then pick a game" isn't built.** Today login
  and game selection are the same step; a platform-level login followed by
  a separate game-switch flow is a named future direction, not current
  scope.

## Messaging

EconomyService publishes an integration event for every state change a future
consumer might care about (`BalanceChangedEvent` and friends), using the
transactional outbox pattern rather than publishing to RabbitMQ directly from
the request path. See [ADR 0003](docs/adr/0003-async-inter-service-communication.md)
for why events instead of a synchronous call, and
[ADR 0010](docs/adr/0010-transactional-outbox-event-bus.md) for the
outbox itself.

**Flow:** `LedgerService` writes an `outbox_messages` row in the same database
transaction as the ledger entry it describes — both commit together, or
neither does. A separate background service (`OutboxDispatcherService`) polls
that table for unsent rows, claiming them with `SELECT ... FOR UPDATE SKIP
LOCKED` so that if EconomyService is ever scaled to multiple replicas, no two
of them publish the same row. Each claimed row is relayed through `IEventBus`
to RabbitMQ and marked `processed_at` once the broker acknowledges it.

**Delivery guarantee:** at-least-once, not exactly-once. A crash between
publishing and marking a row processed causes that message to be redelivered
on the next poll. Deduplicating a redelivered message is the consumer's job -
see below.

**Topology:** a topic exchange named `gbp.economy`, with the routing key set
to the event's type (e.g. `balance.changed`). Topic rather than fanout or
direct, so a consumer added later can bind to just the event types it needs
without the exchange being redeclared. The exchange is declared idempotently
each time the service starts.

### Consumer and inbox-lite deduplication

EconomyService also binds a queue to its own exchange (`balance.changed` and
the three `conversion.*` routing keys) and consumes what it publishes. This
is a demonstration of the delivery loop surviving redelivery, not a
production subscriber - no other service reads these events yet.

Before doing anything with a delivery, the consumer inserts the message's id
into `processed_messages` and applies the delivery's side effect (a
projection counter) in the *same* database transaction. A primary-key
conflict on that insert means an earlier delivery already got here, so the
message is acked and skipped without reprocessing; a crash between the
insert and the commit rolls both back together, so a redelivered message is
reprocessed cleanly rather than silently lost. This is deliberately
**inbox-lite**, not a full inbox pattern - there's no per-message retry
bookkeeping or metadata beyond `message_id` and `processed_at`.

**Known limitations:**
- No dead-letter queue. A row that keeps failing to publish is parked once
  its attempt count hits the configured ceiling — left unsent, logged, and
  no longer retried — rather than routed anywhere for inspection.
- Not exactly-once. See the delivery guarantee above.
- The dispatcher polls on an interval rather than reacting to commits via
  logical replication/CDC, so there is always some delay between a ledger
  entry landing and its event reaching the broker.

## Platform.Worker

A separate Quartz-scheduled project for operational housekeeping, rather
than a timer bolted onto each service. It runs one job today,
`CleanupExpiredTokensJob`, on a 15-minute schedule:

- **identity_db:** deletes expired or already-revoked `refresh_token_families`
  (which cascades to their `refresh_tokens` at the database FK level) and
  expired `email_verification_codes`.
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
- Token signing is HS256 with one symmetric key shared by every service
  that validates tokens. RS256 with a JWKS endpoint is the intended next
  step, not implemented in this slice.
- The game registry (`games` table) lives inside IdentityService's own
  database. It conceptually belongs to a platform-level service, which
  does not exist yet at this stage of the build. See ADR 0005.
- No refresh grace window: a client that loses the network response to a
  legitimate `/refresh` call and retries with the same (now-consumed)
  token is treated as reuse and loses the whole session, not just that
  request. See ADR 0008.
- Verification email is sent synchronously and best-effort. An SMTP
  failure is logged and does not fail registration; `resend-verification`
  is the recovery path. Routing it through a transactional outbox, the
  way EconomyService now does for its own events, is a later extension
  of the same pattern.
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
- The web cookie flow assumes the SPA and the API share an origin, which is
  what lets the refresh cookie use `SameSite=Strict`. A cross-origin
  deployment would need `SameSite=None` plus a CSRF token, neither of which
  is built yet. See ADR 0011.
- A backend-for-frontend would keep even more of the token handling out of
  the browser than the current cookie-only approach, but is more
  infrastructure than this slice justifies.
- The conversion saga is in-process and sequential, not cross-service
  choreography over the message bus — a second service reacting to an
  event mid-saga needs InventoryService, which doesn't exist until slice 3.
  See [ADR 0010's addendum](docs/adr/0010-transactional-outbox-event-bus.md#addendum-the-conversion-saga).
- The deduplicating consumer's inbox is inbox-lite: `message_id` and
  `processed_at`, nothing more. No per-message retry bookkeeping or
  metadata — that's the full inbox pattern, still Extended scope.
- EconomyService validates the same shared HS256 key as IdentityService
  (see above) rather than a key of its own — the same inherited limitation,
  not a new one this service introduces.
- Kubernetes Secrets are plain `Secret` objects applied from the
  `*.secret.example.yaml` templates, not sourced from an external
  secrets manager. Kustomize's `secretGenerator` came up as the natural next
  step here and wasn't built in this group.
- RabbitMQ and both Postgres instances run as dev/sandbox-only images
  (`rabbitmq:4-management-alpine`, `postgres:17-alpine`) in every environment
  this repo currently deploys to. A production target would point at managed
  instances of both instead.