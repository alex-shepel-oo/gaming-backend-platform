# Player-client (Angular)

An Angular 22 workspace under `frontend/` (`shared` library + `player-client`
app) — the first browser client for the platform, covering Login, Games,
Wallet, Convert, Profile, and password reset (a "Forgot password?" link off
the login screen, through to the emailed-link screen). Profile shows real
account data — member-since date, last login, avatar — rather than just
mirroring the JWT's claims, and lets the player edit their display name
and avatar.

## Running it

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

## Cookie flow, client side

The access token lives only in an in-memory signal — never localStorage,
sessionStorage, or a cookie the client can read. The refresh token is the
`gbp_refresh` `httpOnly` cookie IdentityService sets (see
[ADR 0011](../adr/0011-web-auth-cookie-flow.md)); the client never touches
its value, just relies on `withCredentials: true` sending it automatically.
An HTTP interceptor attaches the access token and retries once on 401 after
a refresh; a silent refresh call at bootstrap restores the session on
reload, before routing decides anything. Route guards are UX only, not the
security boundary — see [ADR 0012](../adr/0012-frontend-security-and-guards.md)
for the full reasoning.

## CORS

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
any future direct client. See [ADR 0016](../adr/0016-admin-surface-isolation.md).

## Known limitations

- **`ng serve` has no `proxy.conf.json` yet.** Local dev currently needs
  either that file (pointing `/api` at the gateway) or manually hitting the
  gateway's absolute URL; CORS alone doesn't help until requests are
  actually cross-origin.
