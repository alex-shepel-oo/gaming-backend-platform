# Identity API

All paths below are relative to `http://localhost:5100`. "Auth" is what the
gateway itself enforces; IdentityService applies its own, more granular
policies underneath regardless of what the gateway already checked.

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/api/identity/auth/register` | anonymous | Register an account for a game; 202, email confirmation required |
| POST | `/api/identity/auth/confirm-email` | anonymous | Confirm the code sent by email |
| POST | `/api/identity/auth/resend-verification` | anonymous | Request a new confirmation code |
| POST | `/api/identity/auth/request-password-reset` | anonymous | Request a password reset link by email; 202 regardless of whether the account exists |
| GET | `/api/identity/auth/reset-password/validate` | anonymous | Read-only check of a reset token (`?token=`); 204 if still usable, 400 otherwise. Lets the reset-password page show "this link is invalid or expired" before the player types anything, without consuming the token |
| POST | `/api/identity/auth/reset-password` | anonymous | Complete a password reset using the emailed token; 204, or 400 for any invalid, expired, or already-used token |
| POST | `/api/identity/auth/login` | anonymous | Exchange credentials for a token pair; without `gameSlug`, an account-scoped session with no game attached |
| POST | `/api/identity/auth/select-game` | bearer | Exchange the current session for a game-scoped one for `gameId`, self-joining as `Player` if the caller has no role there yet |
| POST | `/api/identity/auth/refresh` | anonymous | Rotate a refresh token for a new pair (body or cookie, depending on mode) |
| POST | `/api/identity/auth/logout` | anonymous at the gateway, bearer required by the service | Revoke the current session |
| GET | `/api/identity/users/me` | bearer | Current user's profile |
| PATCH | `/api/identity/users/me` | bearer | Update the caller's own `displayName`. `avatarUrl` is read-only from the client's side (see below) - not accepted here even if sent |
| GET | `/api/identity/games/public` | bearer, any player | List active games only, `id`/`slug`/`name` only - the catalog a player picks a game from |
| GET | `/openapi/identity/v1.json` | anonymous | IdentityService's OpenAPI document, proxied through the gateway |
| GET | `/scalar/identity` | anonymous | Interactive API reference (Scalar) |
| GET | `/health` | anonymous | Gateway liveness probe |

Everything above sits on the plain `/api/identity/**` prefix that `player-client`
(and any other non-admin caller) uses. The routes that used to live here —
game management, permission/role management, user search and role
assignment — moved to `/api/admin/identity/**` once `admin-client` got its
own audience-gated surface; see below and
[ADR 0016](../adr/0016-admin-surface-isolation.md).

IdentityService also serves `GET /.well-known/jwks.json` directly, not
proxied through the gateway — this is how Economy, Notification and the
gateway itself fetch the public key they validate tokens against, a
service-to-service call rather than something a frontend ever needs to reach.
See [ADR 0017](../adr/0017-rs256-and-jwks.md).

## Admin API (`/api/admin/identity/**`)

Every route below is additionally gated on `aud=gbp-admin` at the gateway
itself (`RouteClaimsRequirement`) — a `player-client` token never carries
that audience, so it's rejected before the request ever reaches
IdentityService, regardless of what `perms` it happens to hold. The two
`games` routes further require `scope=Platform` at the gateway, since
`platform.games.manage` is a platform-only permission anyway. IdentityService's
own policies (the "Auth" column below) apply on top of that gate exactly as
they did before the move.

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/api/admin/identity/users/{userId}` | bearer | Look up a user in the caller's scope — one game, or platform-wide (moderator and above) |
| GET | `/api/admin/identity/users` | bearer | Search/paginate users in the caller's scope — one game, or platform-wide (moderator and above) |
| POST | `/api/admin/identity/users/{userId}/revoke-sessions` | bearer | Revoke all of a user's sessions (admin) |
| GET | `/api/admin/identity/users/{userId}/roles` | bearer; scope and ownership checked by the service | A user's role in a given scope (`?gameId=`) |
| PATCH | `/api/admin/identity/users/{userId}/roles` | bearer; scope and ownership checked by the service | Assign a role to a user |
| GET | `/api/admin/identity/users/me/games` | bearer | Games the caller personally has a role on — backs `admin-client`'s game picker for a caller with no platform role |
| GET | `/api/admin/identity/permissions` | bearer, moderator or above | The permission catalog — every key the code actually enforces |
| GET | `/api/admin/identity/roles/{role}/permissions` | bearer; scope and ownership checked by the service | A role's effective permissions, optionally scoped to a game (`?gameId=`) |
| PUT | `/api/admin/identity/roles/{role}/permissions` | bearer; scope and ownership checked by the service | Replace a role's permission set |
| GET | `/api/admin/identity/games` | bearer, `platform.games.manage` permission | List registered games, all fields |
| POST | `/api/admin/identity/games` | bearer, `platform.games.manage` permission | Register a new game |
| PATCH | `/api/admin/identity/games/{id}` | bearer; `platform.games.manage`, or `game.metadata.edit` scoped to that game (`IScopeAuthorityGuard` checks either) | Update a game; `name`/`isActive` require `platform.games.manage`, while `description`/`iconUrl` are also open to a game-scoped `game.metadata.edit` caller |
| DELETE | `/api/admin/identity/games/{id}` | bearer, `platform.games.manage` permission | Permanently delete a game; 409 if the game is still active (must be deactivated via the PATCH above first) - see [Game deletion](#game-deletion) below |

## Game deletion

Deleting a game is deliberately two-step: `DELETE` only succeeds once the
game is already `isActive: false` (via the `PATCH` above), which exists as a
forced pause before an irreversible action, not just habit. Within
IdentityService's own database, deleting the `Game` row cascades cleanly to
its `RolePermissions`, `UserGameRoles`, and `RefreshTokenFamilies` (all
`OnDelete(DeleteBehavior.Cascade)` on their `Game` foreign key).

What it does **not** do: clean up that game's currencies, balances, or
conversion history in EconomyService's own database. Games are referenced
there only by a loose `game_id` column, with no cross-service foreign key -
IdentityService has no way to know, let alone clean up, what EconomyService
holds for a game it just deleted. Hard-deleting a game that ever had real
economic activity leaves that data orphaned, pointing at a game that no
longer exists. There's no cleanup path for this yet; the admin UI's delete
confirmation says so explicitly. A proper fix would be IdentityService
publishing a `GameDeleted` event for EconomyService to react to, using the
same outbox/RabbitMQ pipeline already wired up for other cross-service
events - not implemented, since nothing today needs a game actually gone
rather than just deactivated. Full reasoning in
[ADR 0026](../adr/0026-game-hard-delete-orphaned-economy-data.md).

## Avatar URLs

Players used to be able to set an arbitrary `avatarUrl` (any `http(s)` URL,
rendered as an `<img src>` on their profile) through `PATCH /users/me`. That
write path is gone: `UpdateProfileRequest` no longer has an `AvatarUrl`
field at all, so sending one is silently ignored rather than validated. This
was a self-service, self-registering-user-facing surface accepting an
arbitrary external URL with nothing beyond a scheme check
(`UrlValidation.TryNormalize`, still used for admin-only game `iconUrl`
updates) - closing it off was cheaper than building real validation or
moderation for it. A previously-set `avatarUrl` still reads back fine on
`GET /users/me` and still renders; only setting a *new* one is closed. Full
reasoning in [ADR 0025](../adr/0025-close-self-service-avatar-url.md).

## Web auth (cookie mode)

`login`, `refresh` and `logout` above default to slice 1's body-based contract:
both tokens travel in the JSON body, which is what the Postman collection and
any non-browser client still gets. Sending `X-Client-Type: web` switches a
caller onto the cookie-based flow instead: the response body carries only the
access token, meant to be held in memory, while the refresh token is set as an
`httpOnly` cookie the page's own JavaScript never sees. See
[ADR 0011](../adr/0011-web-auth-cookie-flow.md) for the full attribute list
and the SameSite/CORS reasoning.

## Local walkthrough

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

# 6. Reuse the same (now rotated-out) refresh token: rejected with 401
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5100/api/identity/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<refreshToken from step 4>"}'
```

## RBAC walkthrough

Logs in as `demo-racer`'s seeded Game-Admin and reads that role's own
permissions - the kind of call `demo-racer` exists to exercise, scoped to
one game rather than the platform-wide admin `demo-shooter` already had.

```bash
# 1. Log in as demo-racer's Game-Admin (X-Client-Type: admin so the token
#    actually carries aud=gbp-admin - the /api/admin/** call in step 2 is
#    rejected at the gateway otherwise, regardless of the caller's role)
curl -s -X POST http://localhost:5100/api/identity/auth/login \
  -H "Content-Type: application/json" -H "X-Client-Type: admin" \
  -d '{"email":"gameadmin@demo-racer.dev","password":"DemoPassword123!","gameSlug":"demo-racer"}'

# 2. Read the Admin role's permissions for demo-racer (use the accessToken from
#    step 1; demo-racer's seeded id is 00000000-0000-7000-8000-000000000002)
curl -s "http://localhost:5100/api/admin/identity/roles/Admin/permissions?gameId=00000000-0000-7000-8000-000000000002" \
  -H "Authorization: Bearer <accessToken from step 1>"
```

## Password reset, register dedup, and OAuth groundwork

Password reset mirrors email confirmation ([ADR 0009](../adr/0009-email-confirmation-flow.md)):
a high-entropy token, hashed with the same `SHA-256`/`IRefreshTokenGenerator`
pipeline already used for refresh tokens rather than a second hasher, TTL,
single-use, and a uniform `400` on any invalid, expired, or already-consumed
token — the caller can't tell those three apart from the response.
Completing a reset revokes every refresh-token family the user has, in every
game, not only the one the request happened to come from
(`RevocationReason.PasswordChange`) — a stolen password compromises the whole
account, not one game session.

`register` no longer answers a duplicate confirmed account with `409`: that
branch fell into the exact same `202` every other confirmed-user path already
returned once the check was removed, since the response was the only thing
telling an attacker the account existed. A neutral heads-up email now goes
out instead, but only when the account already held a role in that specific
game — a confirmed player joining a second game for the first time takes the
same code path and picks up a new role along the way, and gets no email,
because that's a legitimate self-join, not a repeat attempt.

`external_logins` (provider, provider user id) exists as schema only — no
OAuth provider is wired up against it yet. It's there so a future provider
integration is a service change, not a migration.

Full reasoning in [ADR 0015](../adr/0015-auth-cluster-hardening.md).

## Permission-based RBAC

A role is no longer just a name carried on the token - it's a set of
permissions, resolved fresh at login and refresh time. See
[ADR 0013](../adr/0013-permission-based-rbac-and-audience-scoped-tokens.md)
for the full reasoning behind this shape.

### Catalog and assignments

The catalog of permissions that exist at all is a fixed list of code
constants (`IdentityService/Auth/Permissions.cs`): five `platform.*` keys
that apply across every game (`platform.games.manage`,
`platform.currency.manage`, `platform.roles.manage`, `platform.users.read`,
`platform.balance.adjust`) and five `game.*` keys scoped to one game
(`game.metadata.edit`, `game.currency.manage`, `game.balance.adjust`,
`game.roles.manage`, `game.players.moderate`). Nothing outside that list can
be enforced, on purpose - a permission only exists once some service actually
checks for it.

Which of those keys each role holds is the editable part, kept in a
`role_permissions` table and scoped by `game_id` (`NULL` for a platform-wide
role, a specific game for that role within just that game). A platform
role's authority over every game isn't special-cased anywhere in the
resolver - it's just that Platform-Admin's default rows happen to include
`game.*` keys alongside its `platform.*` ones, both under `game_id = NULL`.

### Token claims

Three claims ride alongside the existing `role` and `game_id`:

- `scope` - `Account`, `Game`, or `Platform`. `Account` is what `login`
  issues without a `gameSlug` - no game attached yet, see
  [Ecosystem-first login](#ecosystem-first-login) below.
- `perms` - the caller's resolved permissions for that session, as an
  array.
- `aud` - `gbp-player` or `gbp-admin`, decided per request from the
  `X-Client-Type` header rather than stored on the token family: `web` gets
  `gbp-player`, `admin` gets `gbp-admin`, anything else (or no header at all)
  defaults to `gbp-player` too. The gateway's `/api/admin/**` routes reject
  anything without `aud=gbp-admin` before IdentityService sees it, so a
  stolen or misdirected player token can't reach admin endpoints just
  because its `perms` would otherwise allow it. See
  [ADR 0016](../adr/0016-admin-surface-isolation.md).

`role` itself is only present on `Game`/`Platform` tokens - an account-scoped
session has no game role to report, and the claim is genuinely absent rather
than carrying a placeholder value.

### Ecosystem-first login

Logging in without a `gameSlug` no longer fails for an ordinary player - it
returns an account-scoped session instead, exactly the same platform-role
check that already decided whether an admin could log in without one. From
there, `POST /auth/select-game { gameId }` exchanges that session for a
game-scoped one, joining as `Player` on the spot if the caller has no role
in that game yet - the same helper `register` already uses to create one,
not a second mechanism. An already game-scoped session can call it too, to
switch games without a fresh login.

Because `account.games.list`/`account.profile.manage` aren't resolved
through `role_permissions` (there's no `(role, game_id)` behind an account
session), they're a fixed pair granted to any authenticated account
regardless of role, not an assignable permission set - they don't show up in
`GET /permissions`'s catalog either, since nothing currently gate-checks
them the way `platform.*`/`game.*` keys actually are.

Web clients hold exactly one refresh cookie: after `select-game`, the
account session is still valid server-side, but the cookie now carries the
game-scoped token, so the browser can't address the account session again
without logging in fresh. Non-web clients, holding both raw tokens
themselves, don't have this limitation - a direct consequence of the
single-cookie design ([ADR 0011](../adr/0011-web-auth-cookie-flow.md)),
not a new trade-off.

Full reasoning in [ADR 0013's ecosystem-first-login addendum](../adr/0013-permission-based-rbac-and-audience-scoped-tokens.md#addendum-ecosystem-first-login).

### Anti-escalation

Editing a role's permissions and assigning a role to a user both go through
the same check: whoever's making the change has to be acting inside their
own scope (their own game's `game.*` rows, or platform-wide rows only with
`platform.roles.manage`), and can only hand out permissions they already
hold themselves. Assigning a role resolves that role's current effective
permissions first and checks those, not the role's name - so granting a
role can't be used as a shortcut around the same guard that a direct
permission edit goes through.

### demo-racer

`demo-racer` is a second seeded game, with its own seeded Game-Admin
(`gameadmin@demo-racer.dev`) scoped to just that game - unlike
`demo-shooter`'s seeded admin, which is platform-wide. It exists to give
the anti-escalation checks above (and any multi-tenant testing) a second
real game to fail against, instead of only ever seeing a lone tenant.
