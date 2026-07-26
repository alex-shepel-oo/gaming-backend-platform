# ADR-0016: Admin surface isolation

- **Status:** Accepted
- **Date:** 2026-07-26

## Context

Platform and game admins/moderators use the same `player-client` bundle players do today — same
origin, same code shipped to every browser, same attack surface. The owner wants admins moved off that
surface entirely, ideally to the point where an ordinary player never has reason to suspect an admin
surface exists at all. Research into back-office isolation as a practice confirms this reduces attack
surface, but only really pays off alongside a barrier that's independent of permissions alone — a
stolen or otherwise-misdirected player token shouldn't work against admin endpoints just because it
happens to carry admin-level `perms`.

ADR-0013 already reserved `aud=gbp-admin` for exactly this moment, unissued until now.

## Decision

**A separate Angular application, `admin-client`, on its own origin — not an `/admin` route inside
`player-client`.** One application covers both platform-wide and game-scoped admins/moderators, with
permission-gated sections inside (platform sections visible by `platform.*` permissions, game sections
by `game.*` permissions for the caller's own game) — not two separate SPAs. The two categories already
share the same underlying permission model; splitting the shell into two applications would cost a
second build pipeline and a second Nginx for a distinction the permission gating already draws cleanly.

**The origin separation reuses the reverse-proxy pattern already proven by `player-client`, not a new
cross-origin design.** The initial draft of this decision assumed a separate admin origin would break
`SameSite=Strict` (ADR-0011) and therefore need `SameSite=None` plus a CSRF token. Checking that
assumption against the actual running system showed it doesn't hold: `player-client`'s own Nginx already
reverse-proxies `/api` onto its own origin specifically so the browser never sees the real,
differently-hosted backend as a separate origin — that's exactly why `SameSite=Strict` already works
there despite the backend living elsewhere. Giving `admin-client` its own Nginx doing the same thing
makes it same-origin with itself too, on a different host — origin separation between the two
*frontends* is achieved by them being genuinely different bundles on different hosts, not by the
backend call being cross-origin. No CSRF middleware, no Angular XSRF wiring; the refresh cookie stays
`SameSite=Strict`, just under its own name (`gbp_admin_refresh`) and its own options section.

**Tokens carry `aud` by client surface, not by fixed configuration.** `X-Client-Type` — already used to
distinguish cookie-mode from body-mode — grows a third value: `web` (player), `admin`, or absent
(non-web). Every token-issuing or -rotating call reads it fresh from the current request, the same way
cookie-vs-body is already decided per-request; `aud` doesn't need a new persisted column on
`RefreshTokenFamily` the way `scope` did, because the client already resends its surface on every
`refresh` call, unlike `scope`, which had no equivalent live signal once `game_id` alone became
ambiguous. Whoever holds a raw refresh token can already choose whatever headers they send, so binding
`aud` to the request instead of the family costs nothing in practice and avoids a migration for no
security gain.

**The gateway gets `/api/admin/**`, `aud`-gated, on a separate CORS policy.** Ocelot has no per-route
CORS of its own (already noted in this repo's own docs), so two policies are applied via explicit
`UseWhen` branches on the path prefix rather than one policy trying to cover both origins. The new
routes point at IdentityService's existing downstream paths — `games`, `permissions`,
`roles/{role}/permissions`, the moderator/admin parts of `users` — nothing new is added to
IdentityService itself, only how the gateway maps and gates access to what already exists. The
player-facing `users/{everything}` wildcard narrows to just `users/me`, the only part of it
`player-client` ever actually calls.

## Alternatives considered

| Alternative | Why it was not chosen |
|---|---|
| `SameSite=None` + CSRF middleware for the admin cookie | Unnecessary once the reverse-proxy pattern already makes the admin cookie same-origin with its own frontend — would add backend Antiforgery wiring and an Angular XSRF integration for a problem that doesn't exist under this design |
| Two separate SPAs (platform-admin, game-admin) | Costs a second shell and pipeline for a split the existing permission-gating already expresses inside one application |
| A new `RefreshTokenFamily.Audience` column | The client already resends its surface via `X-Client-Type` on every refresh — nothing forces re-deriving `aud` from stale, persisted state the way `scope` needed to be, since no ambiguity exists at read time |
| An `/admin` route inside `player-client` | Ships admin code to every player's browser regardless of role — defeats the "players don't even know admins exist" goal outright |

## Consequences

### What we get

Admin tooling stops shipping to every player's browser, and a token minted for one surface stops being
usable against the other even if its `perms` would otherwise allow it — a second, independent barrier
alongside permission checks, not a replacement for them. The isolation is achieved by reusing a pattern
already proven in this codebase (BFF-per-frontend reverse proxy) rather than introducing a new
cross-origin security mechanism this project didn't previously need.

### What it costs

A second full frontend pipeline — build, Dockerfile, Nginx config, Kubernetes manifests, its own CORS
origin entry — where a route inside the existing app would have been cheaper to ship. IP-allowlisting or
MFA for the admin origin is a natural next step this ADR names but doesn't build.

### When this gets revisited

RS256/JWKS (a separate, already-decided piece of future work, ADR-0017) removes the shared HS256 secret
that currently means any validating service could, in principle, mint a token — audience scoping alone
doesn't close that; the two are complementary, not sequenced. If the two admin categories (platform vs.
game) ever need genuinely divergent UX rather than just gated sections, splitting `admin-client` into
two applications later is cheap given the shared `shared` workspace already in place.
