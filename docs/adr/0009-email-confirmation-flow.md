# ADR-0009: Email confirmation flow

- **Status:** Accepted
- **Date:** 2026-07-17

## Context

Registration needs to prove ownership of the email address before an account can be used. The
plan requires a one-time, short-lived verification code as a separate entity — not a JWT — plus a
resend path with its own rate limiting, all runnable locally and in CI without a real email
provider or cloud credentials.

Several design decisions here are not obvious from the endpoints alone and had to be settled
before writing any code, because getting them wrong quietly reopens the account-enumeration and
brute-force problems the rest of the identity flow already closes.

## Decision

**A six-digit numeric code, from `RandomNumberGenerator`, hashed with BCrypt (work factor 12).**
Not SHA-256: a six-digit code has only 10^6 possible values, which SHA-256 walks through in
seconds against a database dump, while refresh tokens (256 bits of entropy) need no such defence.
The rule follows the entropy of the input, not the kind of token — the same reasoning ADR-0008
applies to refresh tokens, applied here to a much smaller space.

**20-minute TTL, 5-attempt limit, single active code per account.** The TTL and the attempt limit
only work as a pair — a six-digit code without an attempt limit is a two-minute brute force
regardless of how short its lifetime is. A partial unique index on `user_id WHERE consumed_at IS
NULL` keeps "one live code per account" as a database invariant rather than a service-layer check,
because the deployment starts at two replicas and a check-then-act in code would be a race the
moment there is more than one process.

**Registration issues no tokens.** `POST /auth/register` returns 202, not 201: the account exists
but cannot be used until confirmed, and handing out a token pair that every gated endpoint would
reject is worse than no credential at all. No refresh family is created at registration — the
first family is created by the first successful login, which is the first moment there is a
session to speak of.

**Confirmation and resend give away nothing about account existence.** An unknown email, a wrong
code, an expired code, a consumed code, and an exhausted-attempts code all return the identical
400. `resend-verification` always returns 202 — for an address with no account, one already
confirmed, and one in cooldown — because an anonymous endpoint that answers differently depending
on whether an account exists is an enumeration oracle with a resend button on it, and a better one
than login since it needs no password.

**Two-level rate limiting on resend.** An in-process IP limiter is the first line, but its state is
per-process and the deployment scales to ten replicas — an in-memory per-account limit would
silently become that limit times the replica count. The limit that actually has to hold — 60
seconds between sends, 5 per hour per account — is derived from the code rows themselves in
Postgres, the one thing every replica agrees on. Login and register keep an in-process-only limit,
accepted because it still slows brute force by orders of magnitude and a database-level account
lock there would be a different kind of risk.

**Confirmation is global to the account, not scoped per game.** A code issued during registration
in game A confirms the email platform-wide (see ADR-0005); registering in a second game with an
already-confirmed address issues no new code and returns 202 with `verificationRequired: false`.

**One `IEmailSender` implementation for every environment.** Azure Communication Services exposes
an SMTP relay, so the same `SmtpEmailSender` (MailKit) that talks to Mailpit locally talks to ACS
in Azure with nothing but configuration changed. A second implementation written only for Azure
and never exercised locally would be the code most likely to break unnoticed. ACS over SendGrid,
to avoid a second vendor and a second bill for one transactional email; a native SDK sender for
better delivery telemetry is a later addition, not a prerequisite.

**Mailpit, not MailHog, as the local SMTP catcher.** MailHog's last release was 2020 — no TLS, no
arm64 images. Mailpit is a drop-in replacement on the same ports with an actively maintained
codebase.

**Email sending is synchronous and best-effort.** No outbox exists yet in this slice (the plan
scopes the Outbox pattern to EconomyService); an SMTP failure is logged and swallowed, and
registration still returns 202 because the code is already in the database and resend is the
compensating path. Superseded: this call site now writes to IdentityService's own outbox instead
of sending inline, and the send itself moved to a separate EmailService — see
[ADR-0024](0024-email-service-extraction.md).

## Alternatives considered

| Option | Why not |
|---|---|
| JWT-based verification link instead of a numeric code | Not revocable or countable the way a database row is, and the requirement is a code read out of an inbox and typed, not a link — there is no frontend in this slice to hide a link behind |
| SHA-256 hash for the code, matching refresh tokens | Appropriate for 256-bit random values, not for a 6-digit space; would be a fast brute force against a database dump |
| Register returns tokens with an `email_confirmed: false` claim | Breaks against the access token's own 15-minute lifetime (ADR-0008): confirming the email doesn't update a token already issued, so the user stays locked out for up to 15 minutes after confirming, until a refresh happens to occur |
| Distinct error codes on resend/confirm for each failure reason | Turns an anonymous endpoint into a free tool for checking whether an account exists or what state its code is in |
| Two `IEmailSender` implementations, one per environment | The Azure-only implementation would never run until the first real deployment, which is the wrong place to discover it doesn't work |
| MailHog | Unmaintained since 2020, no TLS, no arm64 images |

## Consequences

### What we get

An email confirmation flow that cannot be used to enumerate accounts, resists brute force on a
narrow 6-digit space through the TTL/attempt-limit pair, and runs identically in local development,
CI, and Azure with the same code path, verified end to end with no real email account anywhere in
the loop.

### What it costs

Confirmation and resend responses are deliberately uninformative to the caller, which means the
client's UI has to communicate outcomes ("check your inbox") without the backend distinguishing
cases for it. The account-level cooldown lives in the database and adds a query on every resend
attempt. Email delivery has no retry mechanism beyond the user manually requesting a resend.

### When this gets revisited

~~A transactional outbox for email, once EconomyService's outbox pattern is proven and worth
extending.~~ Done — see [ADR-0024](0024-email-service-extraction.md). A native
`Azure.Communication.Email` sender never got built; the platform's production target changed away
from Azure entirely (ADR-0021), so this line is moot rather than pending. A distributed rate
limiter (Redis-backed), if the in-process IP limit on login/register/confirm proves insufficient
once real traffic exists — still open.