# ADR-0020: Frontend distributed tracing with Grafana Faro

- **Status:** Accepted
- **Date:** 2026-07-31

## Context

ADR-0019 wired OpenTelemetry across every backend service and explicitly deferred frontend
instrumentation as a separate follow-up, since there was nothing for a browser-originated trace to
join into until the backend half actually worked. With that landed and verified, a trace still
stopped dead at `api-gateway`'s edge — nothing captured what happened in the browser before a
request was even sent, so a full "click to database and back" trace didn't exist yet.

## Decision

**Instrument both frontend apps with `@grafana/faro-web-sdk` and `@grafana/faro-web-tracing`**, not
a hand-assembled `@opentelemetry/sdk-trace-web` setup — the rest of the stack is already
Grafana-native (Tempo, Loki, Prometheus, Grafana itself, per ADR-0019), and Faro's tracing
instrumentation is itself built on the OpenTelemetry Web SDK, so this is the same underlying
mechanism with less code to own directly. **Only `TracingInstrumentation` is registered — Faro's
bundled error, console, session and Web Vitals capture (`getWebInstrumentations()`) is deliberately
left out.** That data ships in Faro's own event format, which needs a dedicated Faro receiver (e.g.
Grafana Alloy's `faro.receiver` component) to land anywhere useful; standing up a new infrastructure
service to capture data nothing has asked for yet is a decision to make deliberately, not a side
effect of wiring up tracing. Spans export via OTLP/HTTP straight to the same `otel-collector` every
backend service already uses — there is no separate Faro-specific receiver anywhere in this stack.

Wired once, in `frontend/projects/shared/src/lib/telemetry/` (`provideFrontendTelemetry()`), called
from both `player-client` and `admin-client`'s `app.config.ts` — the frontend counterpart to
`AddPlatformTelemetry` on the backend, one shared implementation rather than two copies. A custom
`EnduserIdSpanProcessor` tags every span with the same `enduser.id` attribute the backend already
uses (ADR-0019), set once after login from the JWT claims already available via `TokenStore`, so a
browser-originated trace and a backend trace it continues into carry the same identity marker.

**Two bugs live verification surfaced that a code read alone would not have caught, fixed as part of
this decision rather than left as follow-ups:**

1. **Content-Security-Policy silently blocked the export.** Both apps' Nginx `connect-src 'self'`
   directive doesn't cover `otel-collector`'s origin — the browser blocked the fetch as a CSP
   violation, not a CORS error, which meant Resource Timing showed nothing at all rather than an
   error response. Confirmed via a raw `fetch()` returning `Failed to fetch` with no network entry.
2. **`TracingInstrumentation`'s sampler is hard-wired to a session's `isSampled` flag**, which only
   `SessionInstrumentation` ever sets — with `TracingInstrumentation` alone registered, every span
   was silently marked `NOT_RECORD` and dropped before export, confirmed via the collector's own
   `otelcol_receiver_accepted_spans{transport="http"}` metric staying at zero despite spans clearly
   being created client-side. `SessionInstrumentation` is now registered alongside tracing purely to
   satisfy this — it makes no network call of its own, since `transports` is intentionally left
   empty (see point above about not standing up a Faro receiver).

## Alternatives considered

| Alternative | Why it was not chosen |
|---|---|
| Hand-assembled `@opentelemetry/sdk-trace-web` + `@opentelemetry/instrumentation-fetch`, no Faro | Faro's tracing instrumentation already wraps this; adopting Faro directly is less code to own for the same mechanism, and leaves the door open to its RUM capabilities later without a rewrite |
| Register `getWebInstrumentations()` for full error/session/Web-Vitals capture now | Requires a Faro receiver (Grafana Alloy or equivalent) that doesn't exist in this stack yet — a real new infrastructure component, not something that should appear as a side effect of instrumenting tracing |
| A separate Faro-specific collector/receiver instead of exporting through the existing `otel-collector` | Would duplicate infrastructure that already exists for exactly this purpose (receiving OTLP traces and fanning out to Tempo) |
| Leave `TracingInstrumentation`'s default configuration as-is after spans appeared to export cleanly in initial testing | The collector's own accepted-spans metric was the only thing that revealed every span was actually being dropped server-side despite no client-side error — worth recording as the concrete way this class of bug surfaces |

## Consequences

### Benefits

A trace can now be followed from an actual browser click through `api-gateway` into whichever
backend service handled it, tagged with the acting player's identity the same way backend-only
traces already were. The mechanism is one shared provider, not duplicated per app.

### Trade-offs

**Bundle size**, on top of the pre-existing budget overage both apps already had before this: Faro
alone added roughly 200kB (raw) to each app's initial chunk. Converting every authenticated feature
route to lazy-loading (a real, independent bundle-size problem this work happened to make more
urgent) recovered most of that — `admin-client` now sits below its pre-Faro baseline, `player-client`
is reduced but still over budget. Chasing the remainder (zoneless change detection, auditing Material
module imports) was deliberately left for a later pass rather than expanded into this one.

**No frontend error tracking or Web Vitals yet.** This is an accepted, deliberate gap, not an
oversight — see the Decision section above.

### When to revisit

If frontend error tracking, session replay, or Web Vitals become a real ask, that's the point to
decide on a Faro receiver (Grafana Alloy is the natural choice, staying Grafana-native) rather than
retrofitting the tracing-only setup here. If the remaining bundle-size overage becomes a real problem
rather than a budget-warning annoyance, the deferred optimizations above are the next lever.
