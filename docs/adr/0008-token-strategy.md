# ADR-0008: Token strategy — short-lived access tokens with rotating refresh families

- **Status:** Accepted
- **Date:** 2026-07-17

## Context

The system needs a session model that survives token theft gracefully, supports logout and
admin-initiated revocation, and works across a service that will eventually run several replicas
behind a load balancer (see the HPA manifests planned for Kubernetes). The previous project's
design stored a single JWT on the user row and reissued it by key on "refresh" — no revocation
story, no concept of a session, and no way to distinguish a stolen token from a legitimate one
being reused.

Two open questions had to be settled before any token code could be written: how long an access
token should live, and what happens when a revoked or already-used credential is presented again.

## Decision

**Access tokens live 15 minutes.** Short enough that a leaked token is only useful for a narrow
window; long enough that most user sessions don't need a refresh mid-task. `ClockSkew` is set to
30 seconds, not the framework's 5-minute default — with a 15-minute token, the default skew is a
third of its lifetime and would quietly undermine the point of keeping it short.

**Refresh tokens rotate through families.** A `RefreshTokenFamily` is one login session.
`RefreshToken` is one link in that session's rotation chain: consuming a refresh token marks it
used and issues a new one in the same family. A refresh token is therefore single-use. Presenting
one that has already been consumed means the chain forked — there is no way to tell the
legitimate client from whoever copied the token, so the entire family is revoked and both parties
are forced to log in again.

**The deny-list for access tokens is intentionally narrow.** `RevokedAccessToken` exists for the
one case where a `jti` is actually known at the moment of revocation: logout, where the caller
presents the token being revoked. `revoke-sessions` (admin-initiated) revokes refresh families but
does not populate the deny-list, because the access token `jti`s issued under those families were
never recorded — recording every issued `jti` would mean a database write on every login and every
refresh for a revocation window that is at most 15 minutes wide. Only IdentityService itself reads
this deny-list, on its own `[Authorize]` endpoints. Every other service validates signature and
expiry and does not call Identity synchronously — which is also why the deny-list's blast radius
is bounded by the access token's own short lifetime rather than by anything more aggressive.

**Signing is HS256 with a symmetric key shared by every validating service.** This means any
service that can validate a token could also mint one. Accepted for this slice; RS256 with a JWKS
endpoint is the named next step, not a silent gap.

**Known, accepted cost:** a client that loses the network response to a legitimate `/refresh` call
will retry with the now-consumed token and get treated as a reuse — the whole family is revoked
and the client is logged out. Production systems typically soften this with a short grace window
that replays the same just-issued pair for a few seconds. Not implemented here; naming it is
preferred to silently absorbing the cost.

## Alternatives considered

| Option | Why not |
|---|---|
| Long-lived access tokens (hours), no rotation | Simpler, but a leaked token stays valid for the whole session; no revocation is possible short of waiting it out |
| Refresh tokens without single-use rotation (reusable until expiry) | Removes the only signal that distinguishes a stolen token from a legitimate one — reuse of a consumed token is exactly that signal, and a reusable token throws it away |
| Deny-list every issued access token `jti` at issuance | Closes the revocation window for revoke-sessions too, but costs a database write on every login and every refresh for a benefit bounded by 15 minutes; the `fid` claim already carried in the token gives a cheaper path to this later via family-level revocation once events exist |
| RS256 with JWKS from day one | The correct longer-term answer, but adds a signing-key distribution mechanism this slice's scope doesn't need yet; recorded as the next step rather than built now |
| Grace window on refresh reuse | Reduces false-positive logouts from network retries, but adds a second acceptable-token state (recently-superseded) that has to be reasoned about alongside used/unused/expired; deferred rather than added under time pressure |

## Consequences

### What we get

A stolen refresh token is detectable the moment its owner's next legitimate refresh collides with
it, and the blast radius of that theft — the whole session — is exactly what gets revoked, not
more and not less. Logout and admin revocation both work without any service holding a live
connection to Identity's database.

### What it costs

A dropped network response during refresh costs the user their session, with no automatic
recovery. The signing key is a single shared secret across every service that validates tokens,
which is a wider blast radius than necessary if that key ever leaks. Revoked sessions via
`revoke-sessions` can still authenticate with an already-issued access token for up to 15 minutes.

### When this gets revisited

RS256 + JWKS when a second service needs to validate tokens independently of trusting a shared
secret. A grace window on refresh, if network-retry-induced logouts turn out to be a real user
complaint rather than a theoretical one. Deny-list propagation via RabbitMQ once services need to
react to revocation faster than 15 minutes allows — the `fid` claim is already in the token
specifically so that this extension needs no token format change.