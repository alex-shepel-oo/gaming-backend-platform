# Frontend architecture

An Angular 22 workspace (`frontend/`) with two applications — `player-client` and `admin-client` —
and a `shared` library neither app treats as optional. Full per-app detail lives in
[player-client](../services/player-client.md) and [admin-client](../services/admin-client.md); this
covers the structure both share.

## Workspace shape

```text
frontend/
├── projects/
│   ├── shared/          — services, guards, interceptors, UI components, styles/tokens
│   ├── player-client/   — the public-facing app
│   └── admin-client/    — the platform/game-admin console
```

`player-client` and `admin-client` both import `shared` via its *built* output (`dist/shared`), not
its TypeScript source — `npm run build` builds `shared` first, then each app against that output.
Editing `shared` and expecting a consuming app's dev server to pick it up without a rebuild is the
single most common trap in this workspace.

`shared`'s public surface (`public-api.ts`) is one flat barrel covering four areas both apps draw
from: auth (guards, interceptor, token store, silent session restore), economy (wallet/conversion
services), games/profile/roles/users (typed HTTP clients over the gateway's admin and player-facing
routes), and a small UI kit (`StatusPill`, `EmptyState`, `WipOverlay`, `PageBackground`,
`NotAvailable`, `NotFound`) plus the shared design tokens/typography under `shared/src/styles/`.

## Real request path

```text
Browser
   ↓
Angular app (in-memory access token, gbp_refresh/gbp_admin_refresh httpOnly cookie)
   ↓
HTTP interceptor (attaches Authorization, retries once through TokenRefreshCoordinator on 401)
   ↓
Same-origin Nginx (built image) or CORS-allowed dev server (ng serve)
   ↓
ApiGateway (Ocelot)
   ↓
Backend service
```

The built image is same-origin by design, not an incidental deployment detail: each app's own Nginx
proxies `/api` to `api-gateway` on the same origin the app itself is served from, which is what lets
the refresh cookie use `SameSite=Strict` rather than `None` ([ADR 0011](../adr/0011-web-auth-cookie-flow.md),
[ADR 0012](../adr/0012-frontend-security-and-guards.md)). `ng serve` doesn't go through that proxy, so
local dev relies on the gateway's CORS whitelist instead, or an explicit
`--proxy-config projects/<app>/proxy.conf.json` for a same-origin-like setup without a container.

One path is a genuine exception: `player-client`'s `/hubs` (SignalR) is proxied straight to
`notification-service`, not through `api-gateway`. Ocelot 24.1.0 accepts the WebSocket upgrade and
returns 101 but never pumps frames afterward — confirmed by comparing a direct connection to
`notification-service` (working) against one through the gateway (upgrade stalls, then closes with no
frame ever crossing). See [NotificationService](../services/notification-service.md) for the measured
comparison. `admin-client` has no SignalR usage and no `/hubs` location at all.

## Auth lifecycle

The access token lives only in an in-memory signal (`TokenStore`) — never `localStorage`,
`sessionStorage`, or a client-readable cookie. The refresh token is the `httpOnly` cookie
IdentityService sets; the client never touches its value, only relies on `withCredentials: true`
sending it automatically. A silent refresh call at bootstrap (`provideSilentSessionRestore()`)
restores the session on reload, before routing decides anything. Route guards (`permission.guard`,
`role.guard`, `guest.guard`, `game-scope.guard`) are UX only, not the security boundary — every one
of them exists to avoid rendering a screen the backend would reject anyway, not to replace that
rejection. The backend enforces the real boundary on every request regardless of what the guard
decided ([ADR 0012](../adr/0012-frontend-security-and-guards.md)).

A concurrent-refresh race is worth naming explicitly: several requests failing with 401 at the same
moment used to each trigger their own `POST /auth/refresh`, racing each other for the single-use
refresh token and reliably losing the family for one of them. `TokenRefreshCoordinator` shares one
in-flight refresh across every caller that hits it concurrently instead.

## Build and deploy

`ng build shared && ng build player-client && ng build admin-client` (in that order, enforced by
`npm run build`). Each app builds into its own multi-stage Docker image (`Dockerfile` per app) ending
in an Nginx stage serving the static build and proxying `/api`, `/otlp` (Faro→otel-collector, both
apps), and `/hubs` (player-client only). See
[Backend deployment topology](backend.md) for how these images run in compose vs. Kubernetes, and
[Observability overview](../observability/overview.md) for how Faro's tracing reaches otel-collector
through the same-origin `/otlp` proxy.

## Related documentation

- [player-client](../services/player-client.md), [admin-client](../services/admin-client.md)
- [ADR 0011: Web auth cookie flow](../adr/0011-web-auth-cookie-flow.md)
- [ADR 0012: Frontend security and guards](../adr/0012-frontend-security-and-guards.md)
- [ADR 0020: Frontend tracing with Faro](../adr/0020-frontend-tracing-with-faro.md)
- [Security overview](../security/overview.md)
