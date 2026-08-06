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
- [CD](#cd)
- [CI](#ci)
- [Running locally](#running-locally)
- [Running on Kubernetes](#running-on-kubernetes)
- [Local automation](#local-automation)
- [Identity API](#identity-api)
- [Permission-based RBAC](#permission-based-rbac)
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

`gbargocd.shepel.dev` runs the real GitOps deployment behind this demo (see [CD](#cd) below) but
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

### Login

Authentication screen for the player client.

<img src="docs/screenshots/player/gifs/player-login-register.gif" width="700">

### Wallet

Real-time wallet balance updates delivered through SignalR.

<img src="docs/screenshots/player/gifs/player-wallet.gif" width="700">

### Convert

Converting platform credits to a game currency, with the resulting balance update delivered through SignalR.

<img src="docs/screenshots/player/gifs/player-convert.gif" width="700">

### Games

Game catalog and detail view.

<img src="docs/screenshots/player/player-game-datail.png" width="700">

### Profile

Player profile — account details, member-since date, and avatar.

<img src="docs/screenshots/player/player-account.png" width="700">

</details>

<details>
<summary>Admin Client</summary>

### Login

Authentication screen for the admin client.

<img src="docs/screenshots/admin/admin-login.png" width="700">

### Game Edits

Editing a game's metadata as a platform admin.

<img src="docs/screenshots/admin/gifs/admin-game-edit.gif" width="700">

### Users

Searching users and viewing their account details.

<img src="docs/screenshots/admin/gifs/admin-players.gif" width="700">

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
the request-flow diagram readable) — see [CD](#cd) below for how a change actually reaches this
diagram in production, and [docs/architecture.md](docs/architecture.md) for the full breakdown,
including local-vs-Kubernetes differences and every implemented-vs-planned distinction.

## CD

Argo CD watches `main` and auto-syncs the Helm chart from
[`infra/helm/gaming-backend-platform/`](infra/helm/gaming-backend-platform/) — see
[`scripts/k8s/argocd-application-production.yaml`](scripts/k8s/argocd-application-production.yaml)
for the `Application` itself. It only reacts to `main`, never `develop` or an open PR, and only to
an actual git commit — a fresh image landing in GHCR under the same tag changes nothing about the
rendered manifest on its own.

Each service's own CI workflow ends by pointing that service's `imageTag` — in its own file under
[`image-tags/`](infra/helm/gaming-backend-platform/image-tags/), since CI is path-filtered and a
shared tag would have every service pull an image that was never rebuilt for it — at the commit
SHA it just built, then pushes that change back to `main`, the commit Argo CD's sync actually
reacts to. A push touching only `EconomyService` therefore redeploys exactly `economy-service`
(and `economy-migrator`), not the other seven images sitting untouched. Full reasoning, including
two real RBAC incidents this setup uncovered, in [ADR 0023](docs/adr/0023-gitops-argocd.md).

## CI

| Workflow | Triggers on | Checks |
|---|---|---|
| [identity-ci](.github/workflows/identity-ci.yml) | `backend/IdentityService/**`, `backend/IdentityService.Tests/**`, `backend/Directory.*.props`, `global.json` | `dotnet build` + `dotnet test`, then pushes `identity-service` to GHCR |
| [gateway-ci](.github/workflows/gateway-ci.yml) | `backend/ApiGateway/**`, `backend/ApiGateway.Tests/**`, `backend/Directory.*.props`, `global.json` | `dotnet build` + `dotnet test`, then pushes `api-gateway` to GHCR |
| [economy-ci](.github/workflows/economy-ci.yml) | `backend/EconomyService/**`, `backend/EconomyService.Tests/**`, `backend/Directory.*.props`, `global.json` | `dotnet build` + `dotnet test`, Trivy filesystem scan, pushes `economy-service` to GHCR, then Trivy image scan |
| [platform-worker-ci](.github/workflows/platform-worker-ci.yml) | `backend/Platform.Worker/**`, `backend/Platform.Worker.Tests/**`, `backend/Directory.*.props`, `global.json` | same shape as `economy-ci`, scoped to `platform-worker` |
| [notification-ci](.github/workflows/notification-ci.yml) | `backend/NotificationService/**`, `backend/NotificationService.Tests/**`, `backend/BuildingBlocks.Messaging/**`, `backend/Directory.*.props`, `global.json` | same shape as `economy-ci`, scoped to `notification-service` |
| [email-service-ci](.github/workflows/email-service-ci.yml) | `backend/EmailService/**`, `backend/EmailService.Tests/**`, `backend/Directory.*.props`, `global.json` | same shape as `economy-ci`, scoped to `email-service` |
| [player-client-ci](.github/workflows/player-client-ci.yml) | `frontend/**` | Node 22, `npm ci` + `npm run build` + `npm run test` (Vitest), Trivy filesystem scan, pushes `player-client` to GHCR, then Trivy image scan |
| [admin-client-ci](.github/workflows/admin-client-ci.yml) | `frontend/**` | Same shape as `player-client-ci`, scoped to `admin-client` |
| [k8s-validate](.github/workflows/k8s-validate.yml) | `infra/helm/**` | renders the Helm chart and validates it with `kubeconform` |
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

## Running locally

```
scripts/all/deploy.sh
```

One command: `scripts/all/setup-env.sh` creates `infra/.env` from the example first (generating a
real local JWT signing key if it's still the placeholder), then brings up the whole stack — both
Postgres instances, Consul, RabbitMQ, Mailpit, IdentityService, EconomyService, Platform.Worker,
ApiGateway, player-client and admin-client. See [Local automation](#local-automation) below for
the rest of `scripts/` — this is one entry point among several, not the only thing in there.

Or the same result spelled out manually, without the script:

```
cp infra/.env.example infra/.env
cd infra
docker compose up
```

The player browser client is at `http://localhost:8080`, the admin one at
`http://localhost:8081`; anything hitting the API directly goes through the
gateway at `http://localhost:5100`. Mailpit's UI (for reading verification
emails without a real mailbox) is at `http://localhost:8025`.

Almost every value in `infra/.env.example` is committed on purpose and isn't a
production secret: the stack only binds to `localhost`, so nothing in it is
reachable from outside the machine it runs on, and every clone gets its own
`.env` by copying the example rather than sharing one committed file. The one
exception is `Jwt__PrivateKeyPem` (the RSA key IdentityService signs tokens with,
[ADR 0017](docs/adr/0017-rs256-and-jwks.md)) — that one is deliberately left as
a placeholder, not a working key, since real RSA key material is worth
committing even less than an arbitrary dummy string. Generate your own before
the first `docker compose up`; the comment above that line in `.env.example`
has the one-liner.

## Running on Kubernetes

The chart lives under `infra/helm/gaming-backend-platform/` — one Helm
release, one namespace (`gaming-platform`), the same services as the compose
stack above. `values.yaml` and `values-production.yaml` each carry only the
shape genuinely shared across every service (image defaults, ingress
structure, and so on); the per-service settings live one file per service
under `values/` and `values-production/`, plus one file per CI workflow
under `image-tags/` for the tag each deploy actually pins. See
[ADR 0021](docs/adr/0021-kubernetes-helm-migration.md) for why it's split
this way. `values-local.yaml` layers the knobs specific to the local `kind`
cluster / sandbox namespace this is actually validated against; production
is the real, currently-live deployment behind [the demo links above](#live-demo),
not a placeholder. See
[docs/architecture.md](docs/architecture.md#local-vs-kubernetes) for the full
local-vs-cluster breakdown, including why the environment is pinned to
`Development` here.

A local `kind` cluster needs Traefik as its ingress controller and a couple
of host port mappings to actually front it — `kind`'s own node image ships
neither:

```
kind create cluster --config scripts/k8s/kind-config.yaml
scripts/k8s/install-traefik.sh
```

Secrets are never committed as plaintext, but which mechanism applies depends
on whether the values are throwaway or meant to persist:

For a disposable local cluster, each service still ships a plain template
under `infra/helm/gaming-backend-platform/secrets.example/` — copy, fill in
local values, apply directly, and never check the filled-in copy in:

```
cp infra/helm/gaming-backend-platform/secrets.example/identity.yaml /tmp/identity-secrets.yaml
cp infra/helm/gaming-backend-platform/secrets.example/economy.yaml /tmp/economy-secrets.yaml
cp infra/helm/gaming-backend-platform/secrets.example/rabbitmq.yaml /tmp/rabbitmq-secrets.yaml
# edit each of the three with real values, then:
kubectl create namespace gaming-platform
kubectl apply -f /tmp/identity-secrets.yaml -f /tmp/economy-secrets.yaml -f /tmp/rabbitmq-secrets.yaml
scripts/k8s/apply.sh
```

(`scripts/k8s/up.sh` already automates exactly this for the local `kind`
cluster, generating fresh values into a scratch directory on first run —
nothing above is needed if you're just running the stack locally.)

For values meant to survive a rebuild and actually be reviewable in git —
the real deployment this eventually targets — secrets are encrypted with
[SOPS](https://github.com/getsops/sops) using `age` as the encryption
backend, not left as an unencrypted file someone has to remember to keep out
of version control. `.sops.yaml` at the repo root scopes which paths get
encrypted and with which recipient key:

```yaml
creation_rules:
  - path_regex: infra[\\/]helm[\\/]gaming-backend-platform[\\/]secrets\.enc[\\/].*\.enc\.yaml$
    encrypted_regex: ^(stringData|data)$
    age: age1kl06atlam4ngyp0x8h6d4hv58p7m6qv8xa6gnpewsa64srem9c2q65pvjc
```

`encrypted_regex` keeps only `stringData`'s values ciphertext — `apiVersion`,
`kind`, `metadata` and the `stringData` keys themselves stay legible, so a
`git diff` on one of these files still shows which secret changed even
though the value itself doesn't. The matching private key never lives in the
repo; it sits wherever `sops` looks for it by default
(`$XDG_CONFIG_HOME/sops/age/keys.txt` on Linux/macOS,
`%AppData%\sops\age\keys.txt` on Windows), generated once per operator with
`age-keygen`. Production's own keypair is generated and held the same way,
outside git — the mechanism above is exactly what the live deployment uses,
not a separate one built only to demonstrate it.

`infra/helm/gaming-backend-platform/secrets.enc/` holds the encrypted,
real-value counterparts of the five `secrets.example/` templates, committed
alongside them rather than replacing them — the plain templates remain the
"here's the shape" reference for anyone who hasn't set up an age key yet.
Encrypting a filled-in template and applying it looks like:

```
sops -e -i infra/helm/gaming-backend-platform/secrets.enc/identity.enc.yaml

sops -d infra/helm/gaming-backend-platform/secrets.enc/identity.enc.yaml      | kubectl apply -f -
sops -d infra/helm/gaming-backend-platform/secrets.enc/economy.enc.yaml       | kubectl apply -f -
sops -d infra/helm/gaming-backend-platform/secrets.enc/email-service.enc.yaml | kubectl apply -f -
sops -d infra/helm/gaming-backend-platform/secrets.enc/rabbitmq.enc.yaml      | kubectl apply -f -
sops -d infra/helm/gaming-backend-platform/secrets.enc/grafana.enc.yaml       | kubectl apply -f -
scripts/k8s/apply.sh
```

Piping `sops -d` straight into `kubectl apply -f -` means the decrypted YAML
never touches disk at all; if it does for any reason (debugging a template,
say), delete it once the apply succeeds rather than leaving it next to its
encrypted counterpart.

`gateway`, `economy-service` and `notification-service` validate tokens
against Identity's published JWKS rather than holding any signing secret of
their own — only `identity-secrets` carries the private key
([ADR 0017](docs/adr/0017-rs256-and-jwks.md)). Consul is not deployed at all
here — Kubernetes Services and kube-DNS already provide discovery (ADR 0002).

`scripts/k8s/apply.sh` is a thin `helm upgrade --install` wrapper now, not a
hand-rolled apply order: the two database StatefulSets and the
`identity-migrator`/`economy-migrator` Jobs are `pre-install`/`pre-upgrade`
Helm hooks (see `templates/statefulset.yaml` and `templates/migration-job.yaml`
in the chart), so Helm itself finishes them before any app Deployment —
including `mailpit`, `player-client` and `admin-client` — gets created. A
`Job` still has no `depends_on: condition: service_completed_successfully`
equivalent, but this is Helm's own mechanism for exactly that problem rather
than a wrapper script re-implementing `kubectl wait` by hand. The chart's
`gateway-config` ConfigMap is generated the same way the old Kustomize tree's
was: straight from `backend/ApiGateway/ocelot.Kubernetes.json`, which lives
outside the chart, so `apply.sh` passes it in with `--set-file` rather than
keeping a second copy that could drift.

Reach the stack through the Ingress, fronted by Traefik: every web-facing
service gets its own single-level `*.localhost` hostname, which every major
browser/OS resolves to `127.0.0.1` with zero setup (RFC 6761) — no
`/etc/hosts` entry needed, unlike the old path-plus-one-host layout.

| Host | Routes to |
|---|---|
| `player-client.localhost` | `player-client` (itself proxying `/api` onward to the gateway) |
| `admin-client.localhost` | `admin-client` |
| `mailpit.localhost` | Mailpit's UI (kind/sandbox only) |
| `gateway.localhost` | `api-gateway` directly — convenient for Postman/curl against the API, not something the web clients themselves need |
| `traefik.localhost` | Traefik's own dashboard (`/dashboard/`, trailing slash required) — routers, services and middleware, live |

Seeded demo accounts, all sharing the password `DemoPassword123!` (`DevelopmentSeeder`, this
password only exists on a local/sandbox cluster — never a real deployment):

| Email | Role |
|---|---|
| `admin@demo-shooter.dev` | Platform admin |
| `player.one@demo-shooter.dev`, `player.two@demo-shooter.dev` | Players, `demo-shooter` |
| `gameadmin@demo-racer.dev` | Game admin, `demo-racer` |
| `player.three@demo-racer.dev` | Player, `demo-racer` |

See [docs/architecture.md](docs/architecture.md#local-vs-kubernetes) for why
player-client's own Nginx does the `/api` proxying rather than a second
Ingress rule. Or port-forward directly:

```
kubectl -n gaming-platform port-forward svc/player-client 8080:8080
kubectl -n gaming-platform port-forward svc/admin-client 8081:8081
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
│   ├── setup-env.sh # idempotent: creates infra/.env from the example and fills in
│   │                 # a real local signing key if it's still the placeholder --
│   │                 # every deploy.sh below calls this first
│   ├── verify.sh   # backend build+test, then frontend build+test -- no deploy
│   ├── deploy.sh   # docker compose up -d, the whole stack
│   ├── ci.sh       # verify.sh, then deploy.sh
│   └── stop.sh     # docker compose down (--clean also drops volumes and prunes images)
└── k8s/
    ├── kind-config.yaml           # kind cluster config: host port mappings for Traefik
    ├── install-traefik.sh         # one-time cluster addon install (see above)
    ├── traefik-values-local.yaml  # values for the official Traefik chart on kind
    ├── apply.sh                   # helm upgrade --install the chart (see above)
    └── teardown.sh                # kind delete cluster --name gbp
```

**`verify.sh` vs `ci.sh`:** `verify.sh` is the quick local gate — build and
test both sides and stop there, nothing gets deployed afterward. `ci.sh` is
`verify.sh` plus a full-stack deploy at the end, for when the goal is a
running stack, not just confirmation that everything builds and passes.

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

Port 5003, no database — the service holds nothing that needs to outlive a restart, so a missed push
just leaves a client's balance at its last known value until something else refreshes it. The one thing
it does is turn `BalanceChanged` — already published by EconomyService onto `gbp.economy` for every
ledger-affecting change (see [Messaging](#messaging) and
[ADR 0010](docs/adr/0010-transactional-outbox-event-bus.md)) — into a live push toward whichever browser
tab happens to be connected. EconomyService's publishing side needed no changes to make this work. Full
reasoning in [ADR 0014](docs/adr/0014-notification-service-and-signalr.md).

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/hubs/notifications/negotiate` | bearer | SignalR negotiate; token read from the `Authorization` header, same as every other endpoint |
| GET | `/hubs/notifications` | bearer, token via `access_token` query parameter on this path only | WebSocket/long-polling transport for the hub — a WebSocket handshake can't carry custom headers, so this is the one place in the service that reads a token out of the URL |
| GET | `/health` | anonymous | Liveness probe |
| GET | `/health/ready` | anonymous | Readiness probe (RabbitMQ only — no database to check) |

Auth validates against Identity's published JWKS the same way every other service does
([ADR 0017](docs/adr/0017-rs256-and-jwks.md)), but addressed delivery needs one
extra piece: a custom `IUserIdProvider`. SignalR's default implementation reads
`ClaimTypes.NameIdentifier`, while every token issued in this platform carries the caller's id under the
short claim name `sub` (`MapInboundClaims = false`, already the convention IdentityService and
EconomyService both use). Leave the default provider in place and `Clients.User(id)` matches no one — the
connection still authenticates, the push simply never shows up, and nothing logs an error to point at
why. Worth stating plainly here, since it's the kind of gap that looks fine until someone notices
deliveries aren't landing.

`BalanceChangedConsumer` consumes the queue directly through `IRabbitMqConnection` as a plain
`BackgroundService`, bypassing `BuildingBlocks.Messaging`'s `InboxConsumerBase<TDbContext>`
entirely — that base class ties its dedup step to a database transaction, and this service was built
without one on purpose. There's no dedup layer here at all: if a message gets redelivered, the consumer
just pushes the same, still-current balance a second time, which a connected client re-renders without
any visible effect.

The hub isn't reachable through Ocelot. An actual test against a running gateway showed the WebSocket
upgrade stalling for roughly fifteen seconds before Ocelot tore the connection down, with no SignalR frame
ever getting through — measured, not assumed, against the same request completing in under 200ms with no
proxy in the way. player-client's nginx now proxies `/hubs` straight to `notification-service:5003`
instead, the same direct-proxy approach already in place for `/api`
([ADR 0012](docs/adr/0012-frontend-security-and-guards.md)); see
[ADR 0014](docs/adr/0014-notification-service-and-signalr.md) for the full experiment.

### Known limitations

- **Single replica, no backplane.** SignalR keeps connection state in memory, which doesn't survive
  across replicas without `Microsoft.AspNetCore.SignalR.StackExchangeRedis` or similar. `replicas: 1` is
  this slice's accepted scale, not an unaddressed gap.
- **No notification history.** A client that's disconnected when a balance changes only finds out on its
  next request — kept for Extended scope.
- **A redelivered event can push the same balance twice.** Harmless: the second push repeats a balance
  the client already has, so nothing visibly changes.

## Player-client (Angular)

An Angular 22 workspace under `frontend/` (`shared` library + `player-client`
app) — the first browser client for the platform, covering Login, Games,
Wallet, Convert, Profile, and password reset (a "Forgot password?" link off
the login screen, through to the emailed-link screen). Profile shows real
account data — member-since date, last login, avatar — rather than just
mirroring the JWT's claims, and lets the player edit their display name
and avatar.

### Running it

Built image (matches the demo path, proxies `/api` through Nginx):

```
cd frontend
docker build -f projects/player-client/Dockerfile -t player-client .
docker run -p 8080:8080 --network infra_platform-network player-client
```

Reach it at `http://localhost:8080`. It's also wired into
`infra/docker-compose.yml` as the `player-client` service (port
`PLAYER_CLIENT_PORT`, default `8080`), alongside `admin-client`
(`ADMIN_CLIENT_PORT`, default `8081`).

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

Once logged in, the toolbar balance stays live: `Shell` opens a SignalR
connection to NotificationService right after the initial `refreshBalances()`
load and closes it on logout, and each `balanceChanged` push updates the
shared balance signal directly. `Convert`'s own polling `refreshBalances()`
after a completed conversion is untouched and still runs as a fallback.

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

The gateway runs two named CORS policies, not one shared whitelist:
`PlayerClientCors` (`Cors:AllowedOrigins` — `http://localhost:8080` and
`http://localhost:4200`) and `AdminClientCors` (`AdminCors:AllowedOrigins` —
`http://localhost:8081` and `http://localhost:4201`), both with
`AllowCredentials=true` and `X-Client-Type` in `AllowedHeaders`. Two
`UseWhen` branches pick a policy by path prefix (`/api/admin/**` gets
`AdminClientCors`, everything else gets `PlayerClientCors`) ahead of
`UseOcelot()`, since Ocelot has no per-route CORS config of its own. The
built demo path doesn't need either policy, since each frontend's own Nginx
proxies `/api` onto its own origin — CORS only matters for `ng serve` and
any future direct client. See [ADR 0016](docs/adr/0016-admin-surface-isolation.md).

### Known limitations

- **`ng serve` has no `proxy.conf.json` yet.** Local dev currently needs
  either that file (pointing `/api` at the gateway) or manually hitting the
  gateway's absolute URL; CORS alone doesn't help until requests are
  actually cross-origin.

## Admin-client (Angular)

A second Angular workspace app under `frontend/` (`projects/admin-client`,
sharing the same `shared` library `player-client` does), covering platform
and game admin/moderator tooling that used to be part of `player-client`'s
own reach and now lives entirely off-player-surface instead. One
application, not two — platform-wide sections and game-scoped sections both
live here, gated by permission rather than split into separate SPAs. See
[ADR 0016](docs/adr/0016-admin-surface-isolation.md) for the full reasoning.

### Running it

Built image (matches the demo path, proxies `/api` through its own Nginx,
same shape as `player-client`):

```
cd frontend
docker build -f projects/admin-client/Dockerfile -t admin-client .
docker run -p 8081:8081 --network infra_platform-network admin-client
```

Reach it at `http://localhost:8081`. Local iteration goes through the same
`shared`-then-app build order as `player-client`:

```
cd frontend
npm install
npm run build            # shared first, then each app
npm start -- admin-client # ng serve, http://localhost:4201
npm test                  # Vitest, all projects
```

### Login and the game picker

Login is account-first — there's no game-slug field the way `player-client`
still has one for direct game logins. A caller with a platform-wide role
(`scope=Platform` back from `login`) lands straight in. A caller with only
game-scoped roles gets a game picker instead, backed by
`GET /api/admin/identity/users/me/games` (the games they actually hold a
role on, not the public catalog); picking one calls the same
`POST /api/identity/auth/select-game` player-client's ecosystem-first login
already uses, not a second, admin-only mechanism.

### Cookie flow, client side

Same shape as `player-client`: the access token lives only in an in-memory
signal, and the refresh token is an `httpOnly` cookie the client never reads
— here named `gbp_admin_refresh` rather than `gbp_refresh`, on its own
options section server-side. `admin-client` has its own Nginx doing the same
reverse-proxy trick player-client's does, so the browser sees one origin for
statics and `/api` alike, and the cookie keeps `SameSite=Strict` despite
being a genuinely separate frontend on a different host and port. See
[ADR 0016](docs/adr/0016-admin-surface-isolation.md).

### Screens

- **Games** (`platform.games.manage`) — list/register/update games.
- **Roles** (`platform.roles.manage`) — the permission catalog and each
  role's effective permission set, per game or platform-wide.
- **Users** (Moderator/Admin role tier) — search and look up users in the
  caller's own scope, assign roles, and revoke a user's sessions
  (session revocation itself is Admin-only, stricter than the tier that
  gets into the screen at all); the roster also shows each user's last
  login.
- **My Game** (`game.metadata.edit`) — lets a game-scoped Game-Admin edit
  their own game's `description`/`iconUrl`, nothing else. There's no
  single-game lookup endpoint to back it, so it reuses the same
  `GET /api/admin/identity/users/me/games` call the game picker makes and
  takes the first result, which for this role is a one-element array.

None of this re-implements the backend's anti-escalation rules client-side —
the UI disables a role option it can't confirm the caller is actually
allowed to grant, by asking the same `roles/{role}/permissions` endpoint the
backend's own guard checks against, not a copy of that logic living in the
frontend.

## Messaging

EconomyService publishes an integration event for every state change a future
consumer might care about (`BalanceChangedEvent` and friends), using the
transactional outbox pattern rather than publishing to RabbitMQ directly from
the request path. See [ADR 0003](docs/adr/0003-async-inter-service-communication.md)
for why events instead of a synchronous call,
[ADR 0010](docs/adr/0010-transactional-outbox-event-bus.md) for the
outbox itself, and
[ADR 0018](docs/adr/0018-shared-messaging-building-block.md) for why the
mechanism described below lives in a shared library rather than inside
EconomyService.

The publish/consume machinery itself — `IEventBus`, the outbox writer and
dispatcher, and the inbox dedup base — lives in `BuildingBlocks.Messaging`,
a library shared across services rather than code specific to EconomyService.
The library's boundary is infrastructure only: transport, topology, and the
generic outbox/inbox entities and dispatch/consume mechanics. Domain events
(`BalanceChangedEvent` and friends) and domain side effects (the
`ProjectedEventCount` projection below) stay in EconomyService — the library
never sees them. Each consuming service also keeps its own `outbox_messages`
and `processed_messages` tables in its own database; the library gives every
service the same code to work with, not a shared table, so ADR-0001 still
holds.

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

**Known limitations** (of the shared mechanism, so they apply to every
consumer of `BuildingBlocks.Messaging`, not just EconomyService):
- No dead-letter queue. A row that keeps failing to publish is parked once
  its attempt count hits the configured ceiling — left unsent, logged, and
  no longer retried — rather than routed anywhere for inspection.
- Not exactly-once. See the delivery guarantee above.
- The dispatcher polls on an interval rather than reacting to commits via
  logical replication/CDC, so there is always some delay between a ledger
  entry landing and its event reaching the broker.
- Both IdentityService and EconomyService now fail to start without a
  reachable broker — RabbitMQ went from an EconomyService-only dependency
  to a platform-wide one the moment Identity got its own outbox.

### Welcome grant

IdentityService has its own `outbox_messages` table and its own exchange (`gbp.identity`), populated the
same way EconomyService's is — confirming an email writes a `UserEmailConfirmed` row in the same call
that flips `EmailConfirmed`, no separate transaction needed since that call already goes through one
`SaveChangesAsync`. EconomyService's `UserEmailConfirmedConsumer` binds to that exchange directly — the
first consumer in the system that isn't reading its own service's events — and grants a starting
`PLATFORM_CREDITS` balance through the existing `ILedgerService.GrantAsync`, keyed on
`welcome:{userId}` so a redelivery replays instead of double-granting. Seeded demo users
(`admin`, `player.one`, `player.two`, `gameadmin@demo-racer.dev`, `player.three`) never go through
register/confirm-email, so they get the same balance directly from `EconomyService.DevelopmentSeeder`
instead, addressed by a fixed `UserId` IdentityService's own seeder now assigns them (the same
no-real-foreign-key convention already used for the seeded game ids).

Binding a queue to another service's exchange needed one small change to the shared library:
`InboxConsumerBase<TDbContext>` used to read the exchange to bind from the same `RabbitMqOptions` a
service publishes with, which only ever worked because the one consumer that existed listened to its
own exchange. It now takes the exchange as an explicit argument instead. Full reasoning in
[ADR 0010's welcome-grant addendum](docs/adr/0010-transactional-outbox-event-bus.md#addendum-the-welcome-grant-and-identitys-first-outbox).

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