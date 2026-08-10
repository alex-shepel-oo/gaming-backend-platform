# Security overview

What's actually implemented, not a claim that the platform is "secure" because authentication exists.
Gaps are marked explicitly rather than left implicit.

## Authentication

RS256 JWTs, IdentityService is the only issuer, keys published via `/.well-known/jwks.json` and
cached/refreshed by every validating service through `BuildingBlocks.Auth`
([ADR 0017](../adr/0017-rs256-and-jwks.md)). Access tokens are short-lived (15 minutes); refresh
tokens rotate through single-use families with reuse detection — presenting an already-consumed
refresh token revokes the entire family, not just that one token
([ADR 0008](../adr/0008-token-strategy.md)). Passwords hash with BCrypt.

## Authorization

Permission-based RBAC, not role-name checks: policies check for a specific permission claim
(`platform.games.manage`, `game.balance.adjust`, etc.), scoped per `GameId` where the permission is
game-level rather than platform-wide
([ADR 0013](../adr/0013-permission-based-rbac-and-audience-scoped-tokens.md)). Enforced twice, on
purpose: `ApiGateway`'s `RouteClaimsRequirement` rejects an obviously-wrong-audience request before it
reaches a backend service, and the backend service enforces the same claim itself regardless — a
service trusting the gateway's check alone is one routing mistake away from being open. Admin and
player surfaces use different token audiences (`gbp-admin` vs `gbp-player`,
[ADR 0016](../adr/0016-admin-surface-isolation.md)), so a player token can't even present itself as
admin-shaped.

## Tenant/game isolation

`GameId` is a first-class claim on every token and a first-class column on every scoped entity
([ADR 0005](../adr/0005-multi-tenancy-gameid.md)) — but it's a trusted claim, not independently
re-verified against IdentityService by every service that reads it. See
[Data ownership](../architecture/data.md) for what that trust boundary costs when a game is deleted.

## Rate limiting

In-process, IP-based limits on every auth-adjacent IdentityService endpoint: login, register,
confirm-email, resend-verification, request-password-reset, reset-password. Resend-verification also
has a database-backed per-account cooldown that stays authoritative regardless of how many
gateway/service replicas are running — an in-process-only limit resets per replica, which a
multi-replica deployment would otherwise quietly defeat. No rate limiting exists on EconomyService:
nothing there is reachable pre-authentication, so the same class of abuse (credential
stuffing/enumeration) doesn't apply the same way.

## CORS

Two named policies at the gateway, not one shared whitelist — `PlayerClientCors` and `AdminClientCors`,
each origin-specific with `AllowCredentials=true`
([ADR 0016](../adr/0016-admin-surface-isolation.md)). Matters for `ng serve` and any future direct
client; the built demo images don't rely on it, since each app's own Nginx proxies `/api` same-origin.

## CSRF

No separate anti-CSRF token exists, and none is needed for the actual attack surface: the refresh
cookie is `SameSite=Strict` ([ADR 0011](../adr/0011-web-auth-cookie-flow.md)), which browsers simply
don't attach to a cross-site request in the first place — the standard mitigation for exactly this
class of attack, achieved by the cookie policy rather than a token round-trip.

## Frontend security headers

Both apps' Nginx sets `Content-Security-Policy` (`default-src 'self'`, `script-src 'self'`,
`object-src 'none'`, `frame-ancestors 'none'`), `X-Content-Type-Options: nosniff`,
`X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, and a restrictive
`Permissions-Policy`. `style-src` allows `'unsafe-inline'` because Angular Material injects its CSS as
runtime `<style>` tags; `img-src` allows any `https:` origin because avatar and game/currency icon
URLs point at arbitrary external hosts with no fixed allowlist to check them against.

## Secrets

Never committed as real values — `.env.example` (localhost-only, not a real secret) and
`secrets.example/*.yaml` (placeholder Kubernetes Secret templates) are what's in git; the real
`.env`/Secret is applied directly outside the Helm release and never enters version control. CI runs
`gitleaks` on every push/PR to `main`/`develop` as a backstop against a real secret landing in a
commit anyway.

## Transport

The live demo sits behind Cloudflare in front of the cluster, not exposed directly. The chart's own
Ingress supports TLS conditionally (`ingress.tlsSecretName`) for a cert-manager-issued certificate at
the origin. **Known limitation:** the exact origin-to-Cloudflare TLS mode (full vs. full-strict) isn't
recorded anywhere in this repo — worth confirming and documenting explicitly if this ever needs to
survive someone else operating it.

## Known gaps

- No health check endpoint on EmailService or Platform.Worker (see their own service pages) — not a
  security gap directly, but it means a hung background host has no application-level signal a
  liveness probe could catch.
- No OAuth/social login yet — `ExternalLogins` exists in the `identity_db` schema as groundwork, not a
  working flow.
- No WAF/DDoS-specific configuration beyond whatever Cloudflare provides by default in front of the
  cluster.

## Related documentation

- [ADR 0008: Token strategy](../adr/0008-token-strategy.md)
- [ADR 0011: Web auth cookie flow](../adr/0011-web-auth-cookie-flow.md)
- [ADR 0012: Frontend security and guards](../adr/0012-frontend-security-and-guards.md)
- [ADR 0013: Permission-based RBAC](../adr/0013-permission-based-rbac-and-audience-scoped-tokens.md)
- [ADR 0016: Admin surface isolation](../adr/0016-admin-surface-isolation.md)
- [ADR 0017: RS256 + JWKS](../adr/0017-rs256-and-jwks.md)
- [Data ownership](../architecture/data.md)
