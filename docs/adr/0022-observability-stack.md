# ADR-0022: Observability stack — OpenTelemetry, Grafana, and real infra visibility

- **Status:** Accepted
- **Date:** 2026-08-03

## Context

Slice 3 added inter-service messaging (RabbitMQ) on top of an already multi-service
system, and there was no way to see a request's actual path through it, no aggregated
logs, and no latency/error-rate numbers for any service. Once the real production VPS
deploy happened, that gap widened to include the infrastructure itself — no visibility
into the node's own CPU/memory/disk, or into what each pod was actually consuming.

## Decision

**OpenTelemetry SDK in every service, one otel-collector, three backends, one UI.**
Every service pushes OTLP traces and metrics to a single otel-collector; the collector
fans traces out to Tempo and exposes a Prometheus-scrapeable metrics endpoint. Logs go
to Loki via a Serilog sink. Grafana is the one UI, provisioned with datasources and
dashboards as code (`infra/grafana/provisioning/`, `infra/grafana/dashboards/`) rather
than clicked together by hand.

**Real node/pod resource visibility needed three more pieces**, added once the stack
ran on a real VPS rather than a local `kind` cluster: `node-exporter` (host CPU/
memory/disk), `kube-state-metrics` (Kubernetes object state — pod phase, restart
counts), and per-container CPU/memory through cAdvisor, which every kubelet already
serves but which needs a scoped RBAC grant to reach: a dedicated `ServiceAccount` and
a `ClusterRole` limited to `get`/`list`/`watch` on `nodes`/`nodes/proxy`/
`nodes/metrics`, nothing broader. This is the one place this project's own workloads
hold a Kubernetes API permission beyond what a normal application needs.

**Recruiter-facing read access is a real Grafana Viewer-role account, not Public
Dashboards.** Public Dashboards (share one curated dashboard, no login, no Cloudflare
Access seat) was the first approach — it failed for a real reason, not a
configuration mistake: the "Share externally" drawer in this Grafana version never
resolves the request it needs, confirmed by checking the backend logs during the
attempt (a 404 the drawer treats as fatal, not a real error). A Viewer account created
once via the Admin HTTP API (`POST /api/admin/users`) achieves the same "read-only,
no admin risk" goal and actually works.

## Alternatives considered

| Option | Why not |
|---|---|
| `kube-prometheus-stack` (all-in-one Helm chart) | Opaque defaults and a large surface area versus five small Deployments this project already needed to understand and own individually — the point of a portfolio deployment is demonstrating the pipeline, not installing someone else's |
| A managed APM (Application Insights, Datadog) | Defeats the purpose of demonstrating the OpenTelemetry pipeline itself, and adds a paid external dependency to a demo meant to run indefinitely on a small VPS |
| Cloudflare Access for recruiter-facing Grafana | Rejected on cost: its free tier caps at 50 seats, and a per-account Viewer role gets the same read-only outcome at zero cost and no seat limit |
| Keep retrying Public Dashboards | The failure was reproduced and traced to a real backend request the UI needs never resolving — not a config gap that more clicking would fix |

## Consequences

### What we get

One Grafana instance covers metrics, logs, and traces for every service, plus real
infrastructure-level visibility (node resources, pod restarts, per-container usage) —
not just application metrics. Recruiter-facing read access is documented and actually
works, with no ongoing cost.

### What it costs

Five-plus extra Deployments running permanently on a genuinely small single-node VPS,
mitigated by tight resource requests/limits and the `revisionHistoryLimit` fix in
ADR-0021. The cAdvisor RBAC grant is a real (if narrowly scoped) expansion of what this
project's own service accounts can reach in the Kubernetes API.

### When this gets revisited

If a second node is ever added — the current scrape config assumes a single-node
target for cAdvisor's `kubernetes_sd_configs: role: node` job. Worth revisiting the
Public Dashboards path if a future Grafana upgrade fixes the "Share externally" drawer;
the Viewer-account workaround is not the originally intended mechanism, just the one
that works today.
