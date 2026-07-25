# ADR-0015: Auth-cluster hardening — password reset, register deduplication, OAuth groundwork

- **Status:** Accepted
- **Date:** 2026-07-25

## Context

ADR-0009 already settled the anti-enumeration shape for email confirmation and resend: a uniform
response regardless of whether an account exists, with visibility for abuse handled separately (an
IP-scoped rate limiter) from account state (which stays invisible). Three gaps sat next to that
decision without their own answer: there was no way to recover a forgotten password at all; `register`
leaked account existence through a `409` on a duplicate confirmed email in the same game, which is
exactly the oracle ADR-0009 exists to close; and there was no schema ready to receive an external
identity provider, which the owner wants staged ahead of when it's actually wired up live.

A fourth item — making the resend cooldown visible to the client without leaking per-account state —
turned out, on inspection of the actual code, to already be fully solved by what shipped in slice 1/2:
`ResendAsync` already no-ops silently on every account-state case and always returns `202`; the IP rate
limiter already returns `429` with `Retry-After`. Nothing here needed new backend work; this ADR
records that as a confirmed invariant, not a new mechanism.

## Decision

**Password reset mirrors email confirmation's shape entirely, reusing what refresh tokens already
established rather than inventing a parallel mechanism.** `POST /auth/request-password-reset` responds
`202` unconditionally, exactly as `register`/`resend-verification` do. The reset token itself is
generated and hashed through the *same* `IRefreshTokenGenerator` refresh tokens already use — 32 random
bytes, SHA-256 — rather than a new hasher, since a password-reset token needs exactly the entropy
profile a refresh token already has and none of the low-entropy considerations that made the 6-digit
email code BCrypt-hashed instead. `POST /auth/reset-password` answers the same neutral response for an
invalid, expired, or already-consumed token — no case gets a more specific error than another, the same
principle email confirmation already applies to a wrong or expired code.

**A successful password change revokes every refresh family the account holds, across every game, not
just the session that requested the reset.** A compromised password compromises the whole account, not
one game's session. The bulk-revoke mechanism for this already existed (`RevokeAllSessionsAsync`,
built for the admin `revoke-sessions` endpoint, already capable of revoking across every game when no
specific game is given) but hardcoded `RevocationReason.AdminRevoke` — a reason that predates this flow
and doesn't fit it. Rather than duplicate the bulk-revoke logic under a new method, the existing one
gained a required `reason` parameter (not a default — every caller states its reason explicitly), and
its one existing caller was updated to pass `AdminRevoke` explicitly. Password reset passes
`PasswordChange`, a value that already existed in the enum but had never actually been produced by
anything until now.

**Register's duplicate-email leak is closed by removing the code that created it, not by adding new
branching.** The only place `register` distinguished a duplicate account from a new one was a `409`
thrown when an email was already confirmed and already held a role in the game being registered for —
the handling beneath that branch already did the right thing (no second role, the same `202` shape as
every other path) once the exception stopped intercepting it first. The one thing genuinely new is a
best-effort neutral notice email ("someone tried to register with this address") — and it fires *only*
on a real repeat of the same game, never when an already-confirmed account is joining a second game for
the first time. Those are different events: the first is what the leak used to expose, the second is an
ordinary multi-game self-join that has nothing suspicious about it and shouldn't warn the player as if
it did.

**OAuth gets a schema, not a live integration.** A new `external_logins` table (`provider`,
`provider_user_id`, `user_id`, unique on the pair) is ready to receive a provider without a migration
when one is actually wired up. The linking policy is decided now so it isn't left ambiguous later:
if the provider's email matches an existing **confirmed** local account, auto-link — the provider has
already verified that email on its own, and trusting a verified third-party assertion is reasonable.
If no account exists, create one with `EmailConfirmed = true` outright, skipping the internal
confirmation step the provider has already done externally. A conflict with an existing
**unconfirmed** local account resolves the same way once the provider's confirmation arrives — link,
don't reject; rejecting produces stuck accounts nobody can recover on their own.

## Alternatives considered

| Alternative | Why it was not chosen |
|---|---|
| A distinct hasher/token scheme for password reset | The refresh-token generator already produces exactly the right entropy and hash algorithm for a high-entropy random token; a second implementation of the same primitive is duplication without a reason |
| A new dedicated bulk-revoke method for password reset, leaving `RevokeAllSessionsAsync` untouched | Duplicates logic that already does what's needed (revoke every family, every game) for the sake of avoiding one signature change |
| `409`/distinguishing response for a duplicate `register` | Exactly the enumeration oracle ADR-0009 already rejected for the same reason, applied to a different endpoint |
| Send the neutral "someone tried to register" notice on every already-confirmed-email register attempt, including cross-game self-joins | Would warn ordinary multi-game players about their own legitimate action as if it were suspicious activity |
| Reject an OAuth login that collides with an existing unconfirmed local account | Produces an account the owner can no longer reach through either path — link once the provider confirms instead |
| Build the resend-cooldown visibility mechanism fresh in this group | Already fully present since slice 1/2 — confirmed by reading the actual `ResendAsync` and the existing rate-limit test, not assumed from the plan text |

## Consequences

### What we get

A forgotten password is recoverable through the same anti-enumeration discipline every other auth
endpoint already follows, with no new class of oracle introduced. Register no longer answers
differently for a duplicate than for a fresh signup. The account-wide session revocation on password
change closes the actual security gap a compromised password represents, not just the one game session
in front of the user at the time. OAuth can be turned on later without a schema migration blocking it.

### What it costs

Password-reset links require a new `FrontendBaseUrl` configuration value that nothing in the service
needed before — email confirmation only ever embedded a bare code, never a URL. The neutral
already-registered notice is best-effort and synchronous, the same accepted trade-off email
confirmation already makes (an SMTP failure is logged, not surfaced, and doesn't block the response).
OAuth remains entirely unusable until a later, separate piece of work actually wires up a provider —
this group ships the destination, not the integration.

### When this gets revisited

When a real OAuth provider is actually wired up, using the schema and linking policy fixed here. If a
grace window or backplane-style faster revocation propagation is ever built (ADR-0008's named next
steps), password-change revocation benefits from the same mechanism without changing anything specific
to this flow.
