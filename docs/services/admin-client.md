# Admin-client (Angular)

A second Angular workspace app under `frontend/` (`projects/admin-client`,
sharing the same `shared` library `player-client` does), covering platform
and game admin/moderator tooling that used to be part of `player-client`'s
own reach and now lives entirely off-player-surface instead. One
application, not two — platform-wide sections and game-scoped sections both
live here, gated by permission rather than split into separate SPAs. See
[ADR 0016](../adr/0016-admin-surface-isolation.md) for the full reasoning.

## Running it

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

## Login and the game picker

Login is account-first — there's no game-slug field the way `player-client`
still has one for direct game logins. A caller with a platform-wide role
(`scope=Platform` back from `login`) lands straight in. A caller with only
game-scoped roles gets a game picker instead, backed by
`GET /api/admin/identity/users/me/games` (the games they actually hold a
role on, not the public catalog); picking one calls the same
`POST /api/identity/auth/select-game` player-client's ecosystem-first login
already uses, not a second, admin-only mechanism.

## Cookie flow, client side

Same shape as `player-client`: the access token lives only in an in-memory
signal, and the refresh token is an `httpOnly` cookie the client never reads
— here named `gbp_admin_refresh` rather than `gbp_refresh`, on its own
options section server-side. `admin-client` has its own Nginx doing the same
reverse-proxy trick player-client's does, so the browser sees one origin for
statics and `/api` alike, and the cookie keeps `SameSite=Strict` despite
being a genuinely separate frontend on a different host and port. See
[ADR 0016](../adr/0016-admin-surface-isolation.md).

## Screens

- **Games** (`platform.games.manage`) — list/register/update games, plus a
  one-click deactivate toggle and a delete action in the table itself
  (delete only enables once a game is already inactive — see
  [identity API's Game deletion section](../api/identity.md#game-deletion)
  for why that's a hard gate, not just a confirmation dialog).
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
