# ADR-0005: Multi-tenancy via GameId

- **Status:** Accepted
- **Date:** 2026-07-17

## Context

The platform is positioned as a backend serving several independent Unity games through one SDK,
not as the backend of a single game. That positioning only holds if a person can have one account
across multiple games; if accounts are scoped per game, the platform is really N disconnected
copies of the same service wearing one repository.

Two account models are possible:

- **(A) Tenant-scoped user** — `User.GameId`, email unique within a game. Simple, but the same
  person registering in two games ends up with two unrelated accounts, and "platform hosting
  several games" stops being true beyond the ADR that claims it.
- **(B) Global account + per-game membership** — `User` is global (email unique platform-wide),
  roles are granted per (user, game) pair, and access tokens are issued in the context of a
  specific game, carrying a `game_id` claim.

`GameId` still needs to be a first-class field everywhere it matters — in tokens, in sessions, in
role assignments — the question this ADR settles is where on the identity model it lives, not
whether it exists.

## Decision

**(B).** `User` is global to the platform. `UserGameRole` links a user to a game with a role
(`Player` / `Moderator` / `Admin`); a nullable `game_id` on that table represents a platform-wide
role. `RefreshTokenFamily` — one login session — also carries the game context it was created for.
Access tokens carry a `game_id` claim (absent for a platform-wide session) and a `role` claim
scoped to that game.

`GameId` is first-class exactly where authorization and session state live — not on the `User`
entity itself, which stays free of any single tenant.

**Consequence accepted deliberately:** the `games` table (the tenant registry) lives in
IdentityService's own database for now, even though a registry of tenants conceptually belongs to
a platform-level or admin service. No such service exists yet at this stage of the build, and
Identity is the only service that needs the registry at login time. This is recorded in the
README's known limitations rather than left implicit.

**Test that proves this rather than asserts it:** `TenantIsolationTests` — a token issued for
game A must not see or resolve users belonging only to game B (`GET /users` excludes them,
`GET /users/{id}` returns 404, not 403, to avoid confirming the account exists elsewhere).

## Alternatives considered

| Option | Why not |
|---|---|
| (A) Tenant-scoped user, `User.GameId`, email unique per game | Contradicts the platform's own positioning; a second game for the same person is a second, disconnected identity rather than a second membership |
| Global user with roles as a flat list, no `game_id` on refresh sessions | Loses the tenant context at the session level — a stolen refresh token's blast radius and a login's scope would no longer be traceable to a specific game without an extra join every time |
| Separate tenant registry service, `games` table owned there from day one | The correct end state, but no such service exists in this slice; introducing one now to hold a single lookup table used only by Identity would be premature scope for the sake of purity |

## Consequences

### What we get

One account, one email, one password, usable across every game on the platform, with per-game
roles that can differ (Player in one game, Moderator in another) and per-game sessions that can be
revoked independently.

### What it costs

Identity temporarily owns a piece of platform-level data (the game registry) that belongs
elsewhere in the target architecture. Every query that needs to scope by tenant (user listing,
role checks) must explicitly filter by `game_id` — there is no database-level tenant boundary
enforcing it beyond what the service layer does, so a missing filter is a real category of bug to
guard against in review and in tests, not something the schema prevents by itself.

### When this gets revisited

When a platform-level service (or the `Extended`-scope second implemented tenant work) makes it
worth extracting the game registry out of IdentityService into its own owner.


## Addendum: an explicit `scope` claim formalizes the platform-vs-game axis (slice 3)

### Context

The decision above leans entirely on `game_id`'s presence or absence to distinguish a platform-wide
session from a game-scoped one — accurate, but implicit: a token consumer has to know that "no
`game_id`" means "platform-wide," not "forgot to set it" or some other unstated third case. Slice 3's
permission-based RBAC ([ADR-0013](0013-permission-based-rbac-and-audience-scoped-tokens.md)) needed to
name that distinction directly rather than keep inferring it from a null check, and also needed room for
a third case this ADR never anticipated — an ecosystem-wide session with no game chosen yet at all,
which is not the same thing as a platform-wide administrative session.

### Decision

Access tokens now carry an explicit `scope` claim (`game` / `platform`, with `account` reserved for the
ecosystem-first session slice 3 introduces) alongside `game_id`, rather than leaving the distinction to
be re-derived from `game_id`'s nullability every time it matters. `game_id` itself is unchanged — still
present for a game session, absent for a platform one — `scope` just names the fact instead of leaving a
reader to infer it. See ADR-0013 for the full claim set and the permission model built on top of it.

### Consequences

**Gained:** the platform-vs-game axis this ADR established is now a named claim, not a convention every
new claim-reader has to already know. **Given up / accepted:** nothing changes about where `game_id`
lives or what it means — this addendum only adds a second, explicit signal for the same distinction,
it doesn't relocate or redefine it.

See also [ADR-0021](0021-kubernetes-helm-migration.md) — this ADR is purely a data-modeling decision
and predates the Kubernetes migration by several weeks, but nothing about it changed once the
platform moved onto a cluster; `game_id` and the tenant registry it describes work the same way
regardless of what's hosting the database underneath.