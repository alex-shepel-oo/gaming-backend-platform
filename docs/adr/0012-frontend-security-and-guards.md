# ADR-0012: Frontend security model — in-memory access, cookie refresh, guard as UX not authz

## Status

- **Status:** Accepted
- **Date:** 2026-07-20

## Context

Slice 2 introduces the project's first browser client, player-client, built on Angular 22. It consumes
the cookie-based web auth mode ADR-0011 added to IdentityService: the access token comes back in the
response body, the refresh token arrives as an HttpOnly cookie the client never touches directly. That
decision fixed how the *server* hands out tokens. This ADR fixes how the *client* holds onto them, and
what a route guard is and isn't allowed to mean.

A second, unrelated gap surfaced while building this: the demo's definition of done expects the app to
work correctly when accessed directly at `http://localhost:8080` (the Nginx-served player-client), not
only behind a future Kubernetes ingress. Since `:8080` and the gateway's port are different origins as
far as a browser is concerned, ADR-0011's `SameSite=Strict` cookie — which only works same-origin — needed
that origin question settled now, not deferred to whenever ingress work happens.

## Decision

**Access token: in-memory only, never persisted.** It lives in a private Angular signal and nowhere
else — not localStorage, not sessionStorage, not a cookie the client can read. This is the same reasoning
ADR-0011 already applied to the refresh token, carried through to the client side: anything a script can
read, a successful XSS payload can read too. The cost is that the access token doesn't survive a page
reload — accepted, and addressed below.

**Every API call sends credentials.** `withCredentials: true` is set globally on the HTTP client, so the
refresh cookie actually accompanies requests to the gateway; without it the cookie-based flow silently
does nothing.

**A single 401 → refresh → retry, not a loop.** The HTTP interceptor attaches the access token from the
signal, and on a 401 it calls refresh exactly once and retries the original request once. If the refresh
itself fails — an expired or reused refresh cookie, the same reuse-detection ADR-0008 already enforces
server-side — the client clears its state and stops, rather than retrying indefinitely against a session
that's already dead. Tokens are never written to the console or to structured logs, at any point in this
pipeline.

**Silent refresh runs at bootstrap, before routing decides anything.** Because the access token doesn't
survive a reload, the app calls refresh once at startup and restores the signal from whatever the
httpOnly cookie still allows. If there's no valid cookie, this fails quietly — an unauthenticated visitor
is not an error, it's the normal starting state. Doing this before the router evaluates guards matters: a
guard evaluated before the restore attempt would see "not authenticated" even for a session that's
actually still valid, and bounce the user to login unnecessarily.

**Route guards are UX, not the security boundary.** A guard decides what to show — it keeps an
unauthenticated visitor off the Wallet screen, say — but it is not where authorization actually happens.
The backend rejects an unauthorized request regardless of what any guard decided; a guard is a client-side
convenience so the user doesn't see a screen just to have every request on it bounce. This is stated
explicitly because it's an easy thing to get backwards: a guard that *looks* like a security gate can
create the false impression that routing enforces access, when the actual enforcement is entirely
server-side.

**Content-Security-Policy and security headers, served by Nginx.** `default-src 'self'`, with
`unsafe-inline` avoided wherever Angular 22 allows it (which is most places), plus
`X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, and a minimal `Permissions-Policy`. These
are static, server-set headers — nothing here depends on application code getting it right per request.

**Nginx reverse-proxies `/api` to the gateway, rather than waiting for a future ingress to unify origins.**
The plan's own port table serves player-client on `:8080`, separate from the gateway, and the demo's
definition of done explicitly tests `:8080` directly. Two different ports are two different origins to a
browser, and `SameSite=Strict` — the default this project committed to in ADR-0011 — only survives a
same-site request. So `:8080`'s Nginx forwards `/api/*` to the gateway internally; the browser only ever
sees one origin. This is the same shape the plan describes for the eventual Kubernetes ingress (`/` to the
frontend, `/api` to the gateway) — just built into the Nginx layer now, because the direct-access path
needs to work today, not once ingress exists. Strict CORS on the gateway itself is kept anyway, for the
paths that don't go through this proxy: local `ng serve` during development, and any future direct client
that isn't player-client (an admin panel on a different origin, in slice 3).

## Consequences

**Gained:** the access token is unreachable by a successful XSS payload just like the refresh token
already was; a guard failure can never be mistaken for a security control, because it was never one; the
`:8080` direct-access path actually works with `Strict` cookies instead of silently breaking on reload.

**Given up / accepted:**
- The access token doesn't survive a reload on its own — every page load pays the cost of one silent
  refresh call before the app is fully interactive.
- Route guards add no security value on their own; skipping them wouldn't be a security regression, only
  a worse user experience. This has to be remembered by everyone touching routing later, or a guard risks
  being trusted for something it was never meant to do.
- The Nginx `/api` proxy and gateway CORS are two separate mechanisms that both need to keep working —
  removing either one breaks a different access path (the built demo, or local development), not both at
  once, which makes a regression here easy to miss in one path while testing only the other.
- An admin panel on a genuinely different origin (slice 3) will need its own CORS entry and a fresh look
  at whether `SameSite=Strict` still holds once a second origin is really in play. Answered:
  [ADR-0016](0016-admin-surface-isolation.md) gave admin-client its own Nginx reverse-proxy, the same
  same-origin trick used here, so `Strict` holds unchanged for a second frontend too.