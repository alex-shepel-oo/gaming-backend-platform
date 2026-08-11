# ADR-0024: Email delivery moves out of IdentityService into EmailService

- **Status:** Accepted
- **Date:** 2026-08-03

## Context

ADR-0009 built email confirmation on a synchronous, in-process `IEmailSender` call inside
IdentityService: an SMTP failure was logged and swallowed, registration still returned `202`
regardless, and `resend-verification` was the only recovery path. That ADR's own "When this gets
revisited" section named the exact trigger for changing it — "a transactional outbox for email,
once EconomyService's outbox pattern is proven and worth extending" — and by the time password
reset and the duplicate-registration notice needed the same treatment, IdentityService had three
call sites each choosing between a real send and a swallowed failure on its own.

Kubernetes deployment also changed the shape of the problem: templates rendered from an
`EmbeddedResource` need a rebuild and redeploy to edit copy, where every other config surface in
the chart is now a mounted ConfigMap a deployer can change without touching code.

## Decision

**Email sending becomes its own service, `EmailService`, with no database.** IdentityService keeps
writing to its own outbox exactly as it already did for `UserEmailConfirmedEvent` — confirming an
email, requesting a password reset, and a duplicate-registration notice each now write an
`EmailVerificationRequestedEvent` / `PasswordResetRequestedEvent` /
`DuplicateRegistrationNoticeRequestedEvent` row in the same transaction as the state change they
describe, onto Identity's own `gbp.identity` exchange. `EmailService` binds three queues to that
exchange and does nothing else — no domain logic, no persistence, just render a template and call
an `IEmailSender`.

**No inbox/dedup layer, on purpose.** Every other consumer in this system (`InboxConsumerBase`) or
NotificationService's own `BalanceChangedConsumer`) uses a database row to detect a redelivery.
EmailService has none, so a redelivered message just sends the same email a second time — accepted
because a duplicate confirmation email is a minor annoyance, not a correctness bug, and the
alternative is giving a service whose entire job is "read a queue, call SMTP" a database it would
otherwise never need.

**Templates move from an embedded resource to a ConfigMap-mounted directory**
(`email-service-templates`, `infra/helm/gaming-backend-platform/values.yaml`), read fresh off disk
on every render. Editing copy is now a ConfigMap change, not a rebuild.

**`IEmailSender` keeps ADR-0009's shape — one SMTP implementation for every environment** (MailKit,
talking to Mailpit locally and a real relay in production), plus a `NoopEmailSender` selected by
`Email:Provider` for environments that want the rest of the pipeline exercised without actually
sending anything.

## Alternatives considered

| Option | Why not |
|---|---|
| Keep sending in-process from IdentityService, add an outbox there directly | Would need three separate outbox-writer call sites to coordinate the same retry/at-least-once behavior EmailService gets once, and ties IdentityService's own deploys to email-template changes |
| Give EmailService a database for proper inbox dedup | The one thing this extraction is meant to avoid — a stateless consumer that only renders and sends has no other reason to hold a connection pool or run migrations |
| A dead-letter queue for failed sends | Same accepted gap as the outbox dispatcher and `BalanceChangedConsumer` already carry — a send that can't complete isn't fixed by seeing the same delivery again immediately, and `resend-verification` is still the user-facing recovery path for the one case that matters |

## Consequences

### What we get

Three previously ad-hoc "log and swallow" call sites collapse into one delivery guarantee: an
outbox row and the state change it describes commit together or not at all, same as every other
event in the system. Template edits no longer need a deploy. IdentityService no longer needs an
SMTP client or its dependencies at all.

### What it costs

A redelivered message can send a duplicate email — accepted, not fixed, per the no-dedup decision
above. One more service to run, with its own CI workflow, Helm values file, and Kubernetes
Deployment, for a component with no state and no API surface of its own.

### When this gets revisited

If a delivery failure ever needs to be visibly retried rather than silently dropped (a real
customer-facing incident, not a hypothetical), that's the point to add a dead-letter queue — the
same open question NotificationService and the outbox dispatcher already carry, not new to this
service.
