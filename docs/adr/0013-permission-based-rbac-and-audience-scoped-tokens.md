# ADR-0013: Permission-based RBAC, audience-scoped tokens, and ecosystem-first scope

- **Status:** Accepted
- **Date:** 2026-07-24

## Context

Slice 1/2 gave every session a single `PlatformRole` (`Player`, `Moderator`, `Admin`), carried as one
`role` claim and checked by `RequireClaim` in both services' `AuthorizationPolicies`. The "platform vs.
game" axis already lives in the data — `user_game_roles.game_id` is nullable, with a partial unique
index enforcing at most one platform-wide role per user — but the token never surfaces that distinction:
a platform admin and a game admin both carry `role=Admin`, and nothing in the claim shape lets a service
tell them apart.

Slice 3 needs more than a fourth or fifth role name. The actual requirement is **editable permissions,
with each admin able to edit rights only at their own level** — a game admin must be able to reconfigure
what a game moderator can do in their own game, without being able to touch platform-wide roles or any
other game. A fixed enum can't express that; adding `PlatformAdmin`/`GameAdmin` values to the same enum
would still leave rights hard-coded, and burn the one lever (the enum) the whole system already commits
to.

Separately, every service — Identity, Economy, and the gateway — currently validates incoming tokens
against the same hard-coded `Audience` value, and Identity validates its own tokens as well (it's a
resource server for its own bearer endpoints, not just an issuer). Slice 3 introduces a second surface
(an admin client, arriving in a later group) that needs tokens scoped to that surface specifically, so
a stolen or misdirected player token can't be presented there.

## Decision

**Permission-based RBAC: a role is a named set of permissions; enforcement checks permissions, not role
names.**

- **The permission catalog is a fixed set of code constants**, each scoped `platform.*` (cross-game:
  `platform.games.manage`, `platform.currency.manage`, `platform.roles.manage`, `platform.users.read`,
  `platform.balance.adjust`) or `game.*` (`game.metadata.edit`, `game.currency.manage`,
  `game.balance.adjust`, `game.roles.manage`, `game.players.moderate`). Only permissions the code
  actually enforces exist — nothing lets an admin invent a right in the database that does nothing.
- **Role → permission assignments live in a new `role_permissions` table**, scoped by `game_id`
  (`NULL` = a platform-wide role; a specific `game_id` = that role's permissions within that one game).
  Five roles get seeded default sets — Platform-Admin, Platform-Moderator, Game-Admin, Game-Moderator,
  Player (empty — a player acts only on their own account, checked by `sub`, not by permission).
  **A platform-wide role's power over every game is not a special code path** — it falls straight out of
  the same table: Platform-Admin's default rows include `game.*` keys under `game_id = NULL`, and the
  check constraint that keeps `platform.*` rows pinned to `game_id IS NULL` explicitly allows `game.*`
  permissions at any `game_id`, including `NULL`. One mechanism, no special case.
- **Identity resolves the caller's effective permissions at token issuance and carries them as a
  `perms` claim** (an array). Economy and any future service read `perms` straight off the token —
  no round trip into `identity_db` (ADR-0001 holds: this is the claim shape crossing a service boundary,
  not shared data). Two more claims land alongside it: **`scope`** (`account` / `game` / `platform` —
  disambiguates an ecosystem-wide session, `account`, from a genuinely nonexistent value that today's
  overloaded `game_id IS NULL` conflates with platform-admin) and **`aud`** (`gbp-player` today;
  `gbp-admin` reserved for the admin surface a later group adds).
- **Anti-escalation is one rule, applied uniformly:** an editor may only grant permissions that are (a)
  within their own scope (a game admin only touches `game.*` rows for their own `game_id`; only
  `platform.roles.manage` reaches platform-wide rows or another game's rows) and (b) already present in
  the editor's own `perms`. The same rule governs both editing a role's permission set directly and
  assigning a role to a user — assigning a role grants whatever that role's *current* effective
  permissions are, so the check runs against that resolved set, not the role's name.
- **A platform-scoped token's resource check skips the `game_id` match.** A game-scoped token must match
  its own `game_id` against the resource being touched; a platform-scoped token carries no `game_id` at
  all, so requiring a match would make its `game.*` permissions unusable anywhere — exactly backwards
  from "power over every game." Presence of the permission is sufficient once `scope=platform`.
- **Freshness is bounded by the access token's lifetime, not instant.** A permission change takes effect
  on the caller's next refresh (≤15 minutes, ADR-0008) — refresh already re-resolves the caller's role
  from `user_game_roles` on every rotation, so `perms` inherits that same freshness for free, without new
  machinery. `revoke-sessions` remains the lever for an immediate effect.
- **Changing what `aud` carries is a synchronized change across every validator, not an isolated edit.**
  Identity, Economy, and the gateway all move from a single `ValidAudience` to `ValidAudiences`, accepting
  both `gbp-player` and the reserved (not yet issued) `gbp-admin` in the same change that starts emitting
  `gbp-player` — anything less leaves a window where every token in the system fails validation.

## Alternatives considered

| Alternative | Why it was not chosen |
|---|---|
| Additional fixed roles (`PlatformAdmin`, `GameAdmin`, …) as new enum values | Breaks the existing `role` claim contract and still doesn't deliver editable rights — an admin still can't reconfigure what a moderator can do |
| Resolve permissions per request from `identity_db` | Either a database hit on every request, or a cross-service read into another service's database — both rejected by ADR-0001 and by the cost of a synchronous dependency this architecture otherwise avoids |
| A dedicated `activePlatformAdmin` flag instead of deriving platform reach from `game_id = NULL` rows | Introduces a second source of truth for the same fact the partial unique index already encodes; the existing data model already expresses this correctly |
| Require `game_id` match unconditionally, even for platform-scoped tokens | Makes a platform admin's `game.*` permissions unusable against any actual game — contradicts the requirement that a platform admin has power over every game |

## Consequences

### What we get

Roles become editable within a clear boundary instead of hard-coded: a game admin can reshape their own
game's moderator rights without touching anything outside their game, and a platform admin's reach over
every game is a natural consequence of the data model, not a special case to maintain. Services other
than Identity keep enforcing permissions without any new cross-service dependency — they read `perms`
off a token exactly the way they already read `role`. The audience claim gives a second, independent
barrier against a token minted for one surface being presented at another, ready for the admin surface
before it exists.

### What it costs

**Freshness is bounded, not instant** — a revoked permission stays valid on an already-issued access
token until its next refresh; `revoke-sessions` is the escape hatch, not automatic. **The audience change
touches three services and two independent test fixtures that mint tokens outside `TokenService`** —
this is a wider blast radius than a typical claim addition, and has to land as one atomic change or the
platform is unreachable in between. **`gbp-admin` sits unused for a while** — reserved ahead of the group
that actually issues it, rather than added when needed, to avoid touching this validation code twice.

### When this gets revisited

If the permission catalog grows past a size where a flat list of constants is unwieldy, or if a future
surface needs a permission scope this two-level (`platform`/`game`) model doesn't express (e.g. an
organization or team layer between platform and game), the catalog and the `role_permissions` scoping
column both need a second look. If `revoke-sessions` turns out to be too coarse a tool for the freshness
trade-off in practice, a shorter access-token lifetime is a smaller change than building real-time
permission propagation.


## Addendum: ecosystem-first login

### Context

`scope=account` was reserved above but never issued — logging in without a `gameSlug` was only ever
valid for a platform admin, everyone else got a `403`. The player-client already has a `Login → Games`
flow, but `Login` forced `DEFAULT_GAME_SLUG` on every request, so the account tier this ADR already
named had nothing exercising it. This addendum is the concrete implementation of that reserved value,
not a new decision about what `scope=account` means.

### Decision

**The same check that already distinguished a platform admin from everyone else now decides `account`
vs. `platform`, not `platform` vs. an error.** Logging in without a `gameSlug` still looks for a
platform-wide `user_game_roles` row; found, the session is `scope=platform` exactly as before; not
found, the caller gets an account-scoped session instead of a `403`.

**`RefreshTokenFamily` gains an explicit `Scope` column.** `game_id IS NULL` used to mean exactly one
thing (a platform role); it now has to mean two, so the family records which one directly rather than
having both `login` and `refresh` independently re-derive it from a null check that's no longer
unambiguous. `IssueFamilyAsync` takes `scope` as a required argument, not inferred, not defaulted.

**The `role` claim becomes optional.** An account-scoped session has no game role to report — minting
one anyway (`Player`, say) would misrepresent the token, not simplify it. Its permissions are a fixed
pair, `account.games.list` and `account.profile.manage`, granted to any authenticated account
regardless of role and never resolved through `role_permissions` — that table is shaped for
`(role, game_id)`, and an account session has neither.

**`POST /auth/select-game` exchanges an account session for a game-scoped one, self-joining as `Player`
if the caller has no role there yet.** The same helper `register` already uses to create a player role
on signup creates it here too — self-join is one mechanism serving two entry points, not two. The
existing `login`-with-`gameSlug` path is untouched: it still requires a pre-existing role and still
rejects a caller who lacks one, for clients that haven't moved to the two-step flow.

### Alternatives considered

| Alternative | Why it was not chosen |
|---|---|
| A dedicated flag marking "this is a platform user" instead of reusing the existing platform-role lookup | Duplicates a fact the partial unique index on platform roles already encodes |
| A placeholder role (e.g. `Player`) on account-scoped tokens instead of an optional claim | Would assert a game-level role that doesn't exist — the token would lie about what the caller can do |
| Resolving account-tier permissions through `role_permissions` like every other scope | The table is keyed on `(role, game_id)`; an account session has neither, and every account gets the identical fixed pair regardless |

### Consequences

**`GET /users/me` now returns a `role` that can be `null`.** A deliberate, documented contract change,
not an oversight — any client reading it needs to handle the account-scoped case.

**A web client holds exactly one refresh cookie.** The account session stays valid server-side after
`select-game`, but the cookie now carries the game-scoped token — a web client can't address its own
account session again without logging in fresh, even though the row is still live. Non-web clients,
holding both raw tokens themselves, don't have this limitation. This falls directly out of the
single-cookie design ADR-0011 already chose, not a new trade-off this addendum introduces.
