# Gaming Backend Platform

This is a portfolio project designed to demonstrate practical experience in building modern backend systems using a microservices architecture. It showcases backend development, frontend integration, infrastructure design, deployment, and the operation of distributed applications in a production-like environment.

The project implements a multi-tenant backend platform for games. Each game has its own economy, inventory, progression, and validation rules while leveraging a shared set of backend services and infrastructure. Games integrate through an SDK, enabling them to reuse common functionality while remaining logically isolated.

> **Status:** Slice 2 in progress — cookie-based web auth for IdentityService, EconomyService, player-client (Angular).

[![identity-ci](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/identity-ci.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/identity-ci.yml)
[![gateway-ci](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/gateway-ci.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/gateway-ci.yml)
[![k8s-validate](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/k8s-validate.yml/badge.svg?branch=main)](https://github.com/alex-shepel-oo/gaming-backend-platform/actions/workflows/k8s-validate.yml)

## Tech stack
![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)
![ASP.NET Core](https://img.shields.io/badge/ASP.NET%20Core-Minimal%20APIs-512BD4?logo=dotnet)
![Angular](https://img.shields.io/badge/Angular-20-DD0031?logo=angular)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-17-336791?logo=postgresql)
![RabbitMQ](https://img.shields.io/badge/RabbitMQ-FF6600?logo=rabbitmq)
![Ocelot](https://img.shields.io/badge/Ocelot-API%20Gateway-008080)
![Docker](https://img.shields.io/badge/Docker-2496ED?logo=docker)
![Kubernetes](https://img.shields.io/badge/Kubernetes-326CE5?logo=kubernetes)

## Architecture
[docs/architecture.md](docs/architecture.md).

## Running locally

```
cp infra/.env.example infra/.env
cd infra
docker compose up
```

This brings up Postgres, Consul, Mailpit, IdentityService and ApiGateway.
Everything is reached through the gateway at `http://localhost:5100`; Mailpit's
UI (for reading verification emails without a real mailbox) is at
`http://localhost:8025`.

The values in `infra/.env.example` are committed on purpose and are not
production secrets: the stack only binds to `localhost`, so nothing in it is
reachable from outside the machine it runs on, and every clone gets its own
`.env` by copying the example rather than sharing one committed file.

## Running on Kubernetes

Manifests live under `infra/kubernetes/` (`base/`, `identity/`, `gateway/`,
`mailpit/`). They target a local `kind` cluster or a sandbox namespace, not
production — see [docs/architecture.md](docs/architecture.md#local-vs-kubernetes)
for the full local-vs-cluster breakdown.

```
kubectl apply -f infra/kubernetes/base/
cp infra/kubernetes/identity/secret.example.yaml /tmp/identity-secrets.yaml
# edit /tmp/identity-secrets.yaml with real values, then:
kubectl apply -f /tmp/identity-secrets.yaml
kubectl apply -f infra/kubernetes/identity/
kubectl apply -f infra/kubernetes/gateway/
kubectl apply -f infra/kubernetes/mailpit/   # kind/sandbox only, skip in production
```

`identity/secret.example.yaml` is a template with placeholder values, not a
real Secret — never commit the filled-in copy. The gateway reads its JWT
signing key from that same Secret rather than a copy of its own, and Consul
is not deployed here: Kubernetes Services and kube-DNS already provide
discovery (ADR 0002).

Reach the gateway through its Ingress (assumes `ingress-nginx`), or:

```
kubectl -n gaming-platform port-forward svc/gateway 5100:5100
kubectl -n gaming-platform port-forward svc/mailpit 8025:8025   # kind/sandbox only
```

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

EconomyService is not yet wired behind the gateway (that lands with the CORS/
gateway work later in this slice), so it is reached directly at
`http://localhost:5001`.

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
| GET | `/health` | anonymous | Liveness probe |
| GET | `/health/ready` | anonymous | Readiness probe (database only for now) |

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
  is the recovery path. A transactional outbox for email is a later
  extension of the pattern planned for EconomyService.
- Rate limits on login/register/confirm/resend are enforced per gateway
  replica, not per cluster — the deployment scales to ten replicas, so the
  effective budget is the configured limit times whichever replica count
  is currently running. The per-account cooldown on resend is the
  exception: it is enforced in the database and holds regardless of
  replica count.
- Expired refresh tokens, revoked token families, and expired email
  verification codes are not automatically deleted yet — that's the job
  of `Platform.Worker`'s cleanup jobs, which are Extended scope.
- The web cookie flow assumes the SPA and the API share an origin, which is
  what lets the refresh cookie use `SameSite=Strict`. A cross-origin
  deployment would need `SameSite=None` plus a CSRF token, neither of which
  is built yet. See ADR 0011.
- A backend-for-frontend would keep even more of the token handling out of
  the browser than the current cookie-only approach, but is more
  infrastructure than this slice justifies.
- EconomyService does not publish events for anything it does yet: a
  transactional outbox and a dispatcher are the next piece of this slice.
- Currency conversion is not implemented. `conversion_rates` is seeded and
  readable, but nothing exchanges one currency for another yet; that
  needs the saga work planned for later in this slice.
- EconomyService validates the same shared HS256 key as IdentityService
  (see above) rather than a key of its own — the same inherited limitation,
  not a new one this service introduces.