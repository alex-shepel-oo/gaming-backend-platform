# ADR-0023: GitOps with Argo CD — sync scope, RBAC, and per-service image tags

- **Status:** Accepted
- **Date:** 2026-08-04

## Context

The real production deploy started as a sequence of manual `helm upgrade --install`
runs from an operator's own shell. That worked, but left no audit trail of what was
actually deployed when, and no automatic reconciliation if the live cluster ever
drifted from what git said should be running. Argo CD was installed early in that
process specifically to close this gap, but sat unused for a while — its own
`Application` resource didn't exist yet, so `kubectl get applications` came back
empty: it was reachable and configured, but tracking nothing.

## Decision

**Argo CD watches `main` and auto-syncs on every commit there** — never `develop`,
never an open PR. `scripts/k8s/argocd-apps/gaming-backend-platform.yaml` is the
`Application` that does this; `syncPolicy.automated` was left out until `develop` had
actually merged into `main` and a manual sync had been verified clean by hand, so
turning it on wouldn't immediately revert the live cluster to whatever `main` had
before that merge.

**That `Application` is itself watched by a second, root `Application`**
(`scripts/k8s/argocd-root.yaml`, `directory` source over `scripts/k8s/argocd-apps/`).
The first real chart restructuring after this was written landed on `main` while the
live cluster's `Application` object still held the `valueFiles` list from before that
restructuring — nothing had re-applied it, since a plain Kubernetes resource created by
a one-off `kubectl apply` doesn't update itself just because the file that produced it
changed in git. The root `Application` closes exactly that gap: it's still applied by
hand once, but everything under `argocd-apps/` after that — including edits to this
very file — reaches the cluster on the next auto-sync like any other change.

**Two live incidents shaped the final RBAC and sync-trigger shape:**

**1. No safe public/anonymous read access exists for this Argo CD version.**
Recruiter-facing anonymous access (mirroring the Grafana Viewer pattern from
ADR-0022) was tried first: `users.anonymous.enabled: true` plus a `policy.csv`
explicitly denying every non-`applications` resource type. A live check against every
`/api/v1/*` endpoint with anonymous enabled showed `clusters`, `repositories`,
`certificates`, and `accounts` all still returning full data — including SSH
known-hosts fingerprints and cluster connection details — regardless of those deny
rules. The root cause is not a `policy.csv` authoring mistake: **Argo CD only enforces
RBAC on write actions (`sync`/`delete`/`create`/`update`) for these resource types;
`list`/`get` bypasses it for any authenticated session, anonymous or named.** There is
no `policy.csv` shape that closes this — it would leak the same data to any real
low-privilege login, not only an anonymous one. Decision: anonymous access is off
permanently, and no public Argo CD link exists anywhere this project is documented
(`gbargocd.shepel.dev` has no shared login, unlike Grafana).

**2. `policy.default` applies to every authenticated subject, not just unmapped
ones.** After disabling anonymous access, an explicit `g, admin, role:admin` mapping
was added on top of the same deny-heavy `policy.csv`, expecting the built-in local
`admin` account to now have full access. It still couldn't sync anything —
`argocd account can-i sync applications ...` returned `no`. Argo CD's effect model is
deny-overrides-allow, and `policy.default: role:readonly`'s rules apply to *every*
authenticated subject as a baseline layer, admin included, not only to subjects with
no other role — so the leftover readonly policy from the anonymous-access attempt
was still denying admin's own sync requests, regardless of also being granted
`role:admin`. Fix: removed the custom `policy.csv`/`policy.default` entirely and fell
back to Argo CD's own default RBAC (unrestricted local admin, no anonymous) — correct
and sufficient with exactly one real user of this instance.

**Per-service image tags, split into one file per CI workflow
(`infra/helm/gaming-backend-platform/image-tags/*.yaml`).** CI is path-filtered — a
commit touching only `EconomyService` never rebuilds the other seven images. A single
shared `image.tag` bumped on every merge would have every service pull an image that
was never actually rebuilt under that tag. Instead, each CI workflow's own `Deploy to
production` step (the shared `.github/actions/bump-image-tag` composite action) points
*its own* file's `imageTag` at the commit SHA it just built, only on a push to `main`,
only after its own build succeeded, and pushes that change back to `main` — the
commit Argo CD's auto-sync actually reacts to, not a fresh image landing in GHCR under
a floating tag, which by itself changes nothing about the rendered manifest.

The files started as sections of one shared `values-production.yaml`, with a
reset-and-reapply retry loop (`git fetch`, `git reset --hard origin/main`, reapply the
`yq` edit, retry the push) added after two concurrent workflow runs hit a real content
conflict rebasing a stale local commit. Splitting into 8 separate files removes that
contention structurally — two workflows can never touch the same file — rather than
only surviving it gracefully via retry.

## Alternatives considered

| Option | Why not |
|---|---|
| Fix anonymous access with a more exhaustive `policy.csv` | Verified live that per-resource-type `deny` rules are silently ignored for `list`/`get` on `clusters`/`repositories`/`certificates`/`accounts` regardless of how they're written — an Argo CD RBAC-enforcement gap, not an authoring mistake to iterate past |
| A named "viewer" Argo CD login instead of anonymous, mirroring Grafana | Rejected once the RBAC gap was understood: the leak isn't anonymous-specific — a real low-privilege named account would see the exact same cluster/repo/certificate data through the same unenforced `list`/`get` paths |
| One shared `values-production.yaml` section for all image tags (original design) | Worked, but needed the reset-and-reapply retry loop to survive concurrent CI writers; per-workflow files remove the contention instead of only handling it |
| `git rebase` on push conflict (first retry design) | Handles a clean fast-forward race but not a real content conflict on nearby lines; found live when two runs landed close together and the rebase aborted the whole step instead of retrying |

## Consequences

### What we get

Every production deploy is a real git commit with a real audit trail. A push to `main`
touching one service redeploys exactly that service, not the other seven. The
anonymous-access decision and both RBAC incidents are recorded once here instead of
only living in a file comment someone could "helpfully" revert or shorten later
without knowing why it's there.

### What it costs

No public-facing Argo CD demo link, unlike Grafana — recruiter access to this
project's GitOps tooling is screenshots and description only, not a live login. Eight
small `image-tags/*.yaml` files to keep track of instead of one, though each is
owned by exactly one CI workflow and never touched by hand.

### When this gets revisited

If Argo CD ships proper per-resource RBAC enforcement for `list`/`get` operations in a
future version, the anonymous-access decision is worth revisiting — the reasoning
above is specific to this version's actual enforcement behavior, not a permanent
property of Argo CD. If a second real operator account is ever added, "exactly one
real user" stops being true and RBAC needs to come back — tested with
`argocd account can-i` *before* enabling anything, not after.
