# ADR-0017: RS256 signing and JWKS key distribution

- **Status:** Accepted
- **Date:** 2026-07-27

## Context

ADR-0008 signs access tokens with HS256 and a single symmetric key shared by every service that
validates a token — IdentityService, EconomyService, NotificationService, and the gateway all hold the
same secret today. That ADR already names the cost explicitly: any service that can validate a token
could also mint one, and calls RS256 with a JWKS endpoint "the named next step, not a silent gap." A
compromised downstream service today doesn't just leak data — it can forge a token with whatever
audience and permissions it likes, because it holds the same key Identity signs with.

## Decision

**RS256, with the private key held only by IdentityService.** Every other service moves from holding a
copy of the signing secret to holding nothing at all — they verify against a public key instead.

**IdentityService publishes its public key at `/.well-known/jwks.json`, anonymously readable.** The
response is a JSON Web Key Set (a `{ "keys": [...] }` array) even though exactly one key is active
today — that shape is what lets a second key with a different `kid` show up later during rotation
without changing the response format. Building the actual rotation *process* (minting a second key,
retiring the first once every token issued under it has expired) is future work, not part of this
decision.

**The private key is a deployed secret, not generated at process start.** IdentityService runs multiple
replicas; a key generated fresh on boot would mean every replica signs with a different key, and no
other replica (or downstream validator) would recognize a token any of the others issued. The key is
distributed the same way the old symmetric secret was — a Kubernetes `Secret`/local `.env` value — just
RSA key material instead of an arbitrary string, and now reaching only Identity's own deployment instead
of every validating service's.

**Downstream services fetch and cache the public key themselves, through a small custom resolver, not
a full OpenID Connect discovery document.** `TokenValidationParameters.IssuerSigningKeyResolver` looks
up a key by `kid` from a locally cached copy of the JWKS response, refreshed on a timer. IdentityService
itself skips this path entirely for its own token validation — it already holds the same key pair in
memory for signing, so validating against it directly avoids a pointless network round-trip to its own
endpoint.

**`ValidAlgorithms` is now pinned explicitly to RS256 on every validator, including Identity's own.**
None of the four services pinned this before — closing the classic RS256→HS256 confusion attack (an
attacker submitting the public key back as if it were an HMAC secret) requires every validator to
refuse anything but the one algorithm actually in use, not just the newly-added ones.

## Alternatives considered

| Alternative | Why it was not chosen |
|---|---|
| `MetadataAddress` / full OpenID Connect discovery document | Standard .NET path for OIDC, but expects a `/.well-known/openid-configuration` document pointing at a separate JWKS URL — two endpoints and a discovery protocol for what only needed to be one plain JWKS file |
| Generate the RSA key pair at process startup | Breaks the moment more than one replica is running (confirmed: Identity's own deployment already runs two) — each instance would sign with a key no other instance or validator recognizes |
| Consul-based service discovery for the JWKS fetch | This project already uses Consul for gateway routing, but pulling it into application code for a single outbound HTTP call is a new pattern for a problem a plain per-environment URL already solves |
| Build key rotation now, not just the array shape that supports it later | Nothing in this slice needs an actual second key yet; shipping the mechanism without a process to exercise it would be untested code carried for a hypothetical |

## Consequences

### What we get

A compromised downstream service (Economy, Notification, or the gateway) can no longer mint a token —
it holds no key capable of signing one, only a cached public key it uses to verify. Forging any
token, admin-scoped or otherwise, now requires compromising Identity itself, not any service that
happens to check a bearer header. ADR-0008's HS256 signing section is superseded by this decision, not
merely superseded-with-a-caveat — the shared-secret model it described no longer exists anywhere in the
system.

### What it costs

Three services now depend on Identity's JWKS endpoint being reachable at least once per cache window;
a cache miss on a cold start with Identity unavailable means that service can't validate any token
until Identity answers. The cache absorbs most of this — the same class of trade-off already accepted
for `select-game`/`refresh` depending on Identity's availability. Key rotation, when it's actually
built, adds an operational step (generate, publish alongside the old key, wait out every token's
lifetime under the retiring key, then remove it) that doesn't exist with a single static key.

### When this gets revisited

Actual key rotation tooling, once there's a real reason to rotate (suspected compromise, routine
hygiene) rather than just the JWKS shape that tolerates it. A JWKS response cached across more replicas
of a single validating service could be pulled into a shared distributed cache if the per-instance
memory cache turns out to be too many redundant calls to Identity under real load — not a problem yet
at this scale.
