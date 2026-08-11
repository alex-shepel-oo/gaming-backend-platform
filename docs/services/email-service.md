# EmailService

## Responsibility

Sends every transactional email the platform sends: registration verification codes, password-reset
links, and duplicate-registration notices. Extracted out of IdentityService rather than left inline —
see [ADR 0024](../adr/0024-email-service-extraction.md) for why.

## Architecture

A plain generic `IHost`, not a `WebApplication` — this service has no HTTP surface at all, only three
queue consumers (`EmailVerificationRequestedConsumer`, `PasswordResetRequestedConsumer`,
`DuplicateRegistrationNoticeRequestedConsumer`), each a `QueueConsumerBase`-derived `BackgroundService`
bound to its own queue on the `gbp.identity` exchange. `IEmailSender` is swappable:
`SmtpEmailSender` for a real relay, `NoopEmailSender` for environments with no mail configured.
Templates render through `IEmailTemplateRenderer`/`EmailTemplateRenderer` — literal `{{Token}}`-style
placeholder substitution, no template engine, so every placeholder used by an email
(`backend/EmailService/Templates/*.html`/`.txt`) has to exist verbatim in the message that requests it.

## API

None — no HTTP surface. Configuration only (`EmailOptions`).

## Data

None. Stateless consumer.

## Messaging

Consumes `EmailVerificationRequested`, `PasswordResetRequested`, and
`DuplicateRegistrationNoticeRequested` from IdentityService's outbox, all on the `gbp.identity`
exchange. Publishes nothing. See [Messaging](../messaging.md).

## Dependencies

RabbitMQ (queue consumption), an SMTP relay in production (or none — `NoopEmailSender` locally),
IdentityService only indirectly (its outbox is the event source, no direct HTTP call between them).

## Security

No inbound attack surface — no HTTP listener to secure. The email content itself carries no secrets
beyond the token/link the event payload already provided; templates render server-side, no
user-supplied content is interpolated unescaped into HTML.

## Deployment

Docker image, no `terminationGracePeriodSeconds` surprises: its own `ShutdownTimeout` (15s) sits
comfortably below the chart's default grace period (30s) so an in-flight send finishes before
Kubernetes would otherwise `SIGKILL` it. **No health check endpoint exists** — a plain `IHost` with no
web server has nowhere to expose one; Kubernetes liveness/readiness for this Deployment currently has
no application-level signal to probe. See [Backend deployment topology](../architecture/backend.md).

## Observability

OpenTelemetry via `BuildingBlocks.Telemetry`, Serilog to Console + Loki. No metrics/health beyond
what the trace/log pipeline itself surfaces.

## Related documentation

- [ADR 0024: EmailService extraction](../adr/0024-email-service-extraction.md)
- [Messaging](../messaging.md)
- [ADR 0009: Email confirmation flow](../adr/0009-email-confirmation-flow.md)
