# ADR-0011: Cookie-based refresh for web clients, alongside the existing body-based flow

- **Status:** Accepted
- **Date:** 2026-07-18

## Context

Slice 1 issues both the access and refresh token in the response body. That is the right shape for
API clients (Postman, curl, a future SDK), but the browser is a different threat model: a token a
script can read is a token XSS can steal. Storing the refresh token in localStorage or sessionStorage
puts a long-lived credential exactly where the most common web vulnerability class can reach it.

The player-client is the first browser-based consumer of IdentityService. It needs a session that
survives a reload without keeping the refresh token anywhere JavaScript can touch, without breaking
the body-based contract the slice 1 API surface already promised.

## Decision

Introduce a second, opt-in auth mode alongside the existing one, rather than replacing it.

**Mode selection.** A request header, `X-Client-Type: web`, switches `login`, `refresh` and `logout`
into cookie mode. No header means the slice 1 behavior is unchanged: both tokens in the body. A
separate set of `/web/...` routes was considered and rejected — it would duplicate the route surface
for no behavioral gain, since the only difference is where the refresh token travels.

**Token placement.** In web mode, the access token is returned in the body and is expected to live in
memory on the client (a signal, not storage). The refresh token is never in the body; it is set as an
HttpOnly cookie. A script on the page cannot read it even if it can execute.

**Cookie attributes.**

| Attribute | Value | Why |
|---|---|---|
| Name | `gbp_refresh` | |
| HttpOnly | true | not readable by JS, the entire point of this ADR |
| Secure | `true` in every environment except Development, where `Cookie:RequireSecure=false` is allowed | some browsers refuse `Secure` cookies over plain `http://localhost`; production always requires it |
| SameSite | `Strict` (see below) | |
| Path | `/api/identity/auth` | covers `login`, `refresh` and `logout` under one path, so all three consistently set, read and clear the same cookie |
| Max-Age | refresh token TTL, 14 days (unchanged from slice 1) | |

`Path` was the one place the surrounding planning document contradicted itself — one passage scoped
the cookie to `/auth/refresh` specifically, others to `/api/identity/auth`. A refresh-only path would
mean `logout` never receives the cookie it needs to identify and revoke the session. The broader path
is the only one that lets a single cookie serve all three endpoints, and is what this decision fixes.

**SameSite.** Defaults to `Strict`, driven by configuration rather than hardcoded. The demo topology
puts the player-client and the gateway behind the same origin (the ingress serves both static assets
and `/api`), which is what `Strict` requires to work at all. Silent-refresh on page load — the
mechanism that lets the in-memory access token survive a reload — is a same-site XHR triggered by the
page's own script, not a cross-site navigation, so `Strict` does not interfere with it; it only blocks
a *different* site from causing the cookie to be sent. If the demo deployment turns out to be
cross-origin once slice 2's frontend group ships, this reverts to `SameSite=None` plus `Secure`, and a
CSRF token becomes mandatory — noted as a follow-up, not built preemptively.

**Endpoint behavior.**

| Endpoint | Body mode (slice 1, unchanged) | Web mode (new) |
|---|---|---|
| `login` | `{accessToken, refreshToken}` | `{accessToken}` + `Set-Cookie: gbp_refresh=…` |
| `refresh` | reads `refreshToken` from body, returns a new pair | reads `gbp_refresh` from cookie, returns `{accessToken}` + rotated `Set-Cookie` |
| `logout` | reads `refreshToken` from body | reads `gbp_refresh` from cookie, revokes the family, clears the cookie (`Set-Cookie` with `Max-Age=0`, same `Path`/`SameSite`/`Secure` it was set with) |

Web-mode login runs through the exact same pipeline as body-mode login — password check, the uniform
401 on bad credentials, the `email-not-confirmed` 403 gate from the email confirmation addendum. The
only thing that changes is where the refresh token ends up at the very last step. Nothing about
rotation, reuse detection, or family revocation changes either; those still operate on the presented
refresh token regardless of which transport carried it.

**Public games listing.** A related, smaller gap: the player-client needs a games list to let a user
pick a tenant, but slice 1's `/games` is admin-only. This ADR also covers adding
`GET /api/identity/games/public`, returning only active games and only their `id`, `slug` and `name` —
no admin fields. It requires a bearer token from any authenticated player (not anonymous, and not
scoped to the caller's `game_id` the way the rest of the API is) — it's a platform-wide catalog, not a
per-tenant resource.

**CORS dependency.** Cookie-based refresh only works if the browser is allowed to send credentials
cross-request: the gateway's CORS policy needs `AllowCredentials=true` with an explicit allowed origin
(a wildcard `*` is rejected by browsers once credentials are involved), and `X-Client-Type` needs to be
in the allowed request headers. This lands with the frontend/infra group, not here, but the cookie
design depends on it and would silently fail without it.

## Consequences

**Gained:** the refresh token is no longer reachable by any script running on the page, including a
successful XSS payload. The body-based contract from slice 1 is untouched, so the existing Postman
collection and any future non-browser client keep working exactly as before.

**Given up / accepted:**
- The access token is lost on every page reload. This is intentional — recovered via a silent refresh
  on app init, which the cookie survives.
- `SameSite=Strict` only holds while the deployment is same-origin. If that changes, this ADR needs a
  follow-up revision along with a CSRF token.
- A BFF (backend-for-frontend) pattern would remove even more of the browser's exposure to raw tokens,
  but is more infrastructure than an MVP portfolio slice justifies. Noted as a known limitation, not
  implemented.
- Refresh token rotation, reuse detection and family revocation are inherited unchanged from ADR-0008;
  this ADR does not revisit that mechanism, only its transport for browser clients.
- This ADR covers exactly one cookie, `gbp_refresh`, for the one frontend that existed at the time.
  [ADR-0016](0016-admin-surface-isolation.md) later added a second, independent cookie
  (`gbp_admin_refresh`) for admin-client — a parallel instance of this same design, not a change to
  it.