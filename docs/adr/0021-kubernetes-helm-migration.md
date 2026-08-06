# ADR-0021: Kubernetes deployment — one Helm chart, map-driven

- **Status:** Accepted
- **Date:** 2026-08-03

## Context

Local development ran on a Kustomize tree (`infra/kubernetes/`) with one overlay per
environment. That held up while there was one environment to worry about, but a second,
genuinely different target — a real production VPS, with its own image tags, replica
sizing, secret handling, and seeding behavior — turned every one of those differences
into a Kustomize patch. The patches didn't compose well: a service added later needed
its own base manifest set and its own per-environment patches, doubling the
maintenance surface for something that's structurally the same Deployment/Service/
ConfigMap shape seven times over.

## Decision

**Replace the Kustomize tree with one Helm chart** (`infra/helm/gaming-backend-platform/`),
shared by the local `kind` cluster and the real production deployment, with
`values-local.yaml`/`values-production.yaml` layering only the knobs that actually
differ.

**Deployments and migration Jobs are driven by a map in values, not one template file
per service.** `templates/deployment.yaml` and `templates/migration-job.yaml` each
`range` over `.Values.deployments` / `.Values.migrationJobs`; a new service is a new
map entry with its image name, ports, env, and resource shape — not a new template
file duplicating the same labels, checksums, and probe wiring seven more times.

Three template-level fixes came out of running this for real, all now unconditional
chart behavior rather than per-service special-casing:

- **`replicas` is omitted from the rendered spec when an HPA manages that Deployment.**
  Setting it in both places creates a server-side-apply field-manager conflict the
  moment the HPA has ever scaled the Deployment once — found live as `helm upgrade`
  failing outright with `conflict ... apps/v1, Kind=Deployment ... .spec.replicas`.
- **`strategy: Recreate` applies automatically to any Deployment with a PVC-backed
  volume.** The default `RollingUpdate` starts the new pod before killing the old one;
  on `local-path-provisioner` (a `hostPath` under the hood, single-node here) that
  means two processes pointed at the same on-disk directory at once. Found live:
  Prometheus's own TSDB lock refused a second writer and crash-looped instead of ever
  becoming ready, while the old pod kept running, untouched.
- **`revisionHistoryLimit: 3`.** Kubernetes' own default (10) keeps every old
  ReplicaSet around, scaled to 0, for that many revisions — harmless individually, but
  Grafana alone had six of them after a day of redeploys.

## Alternatives considered

| Option | Why not |
|---|---|
| Keep Kustomize, add a production overlay | The per-environment differences (image tag, replica count, secret source, seeding) are exactly what Helm's values layering already does; a Kustomize overlay would re-derive the same mechanism with more YAML |
| One Helm chart per service (8 charts) | Most of the boilerplate — labels, checksums, probes, the RBAC/PVC fixes above — is identical across services; a single map-driven chart gets that reuse for free instead of copying it into 8 `Chart.yaml`s |
| Raw manifests + `envsubst` for per-environment values | No dry-run diffing, no release history, and a worse fit for Argo CD (ADR-0023), which expects a Helm or Kustomize source natively |

## Consequences

### What we get

One chart drives both environments. Adding a service is a new `deployments` map entry,
not a new template file. The HPA field-manager conflict, the PVC/RollingUpdate crash
loop, and unbounded ReplicaSet history are all fixed once at the template level,
not per-service.

### What it costs

The map-driven template carries more conditional logic (`{{- if $svc.hpa }}`,
`{{- if $svc.volumes }}`, and so on) than a flat per-service template would need. A
genuinely unusual service — one needing a Deployment shape the existing conditionals
don't already express — would strain the shared template rather than just getting its
own file.

### When this gets revisited

If a service needs a fundamentally different workload type (a `DaemonSet`, a
`CronJob`) that the map-driven `Deployment`/`Job` pattern doesn't cover, or if
`templates/deployment.yaml`'s conditional logic grows past the point of being readable
in one pass.
