# Deployment (Kubernetes)

The chart lives under `infra/helm/gaming-backend-platform/` — one Helm release, one namespace
(`gaming-platform`), the same services as the compose stack described in
[Local development](local-development.md). `values.yaml` and `values-production.yaml` each carry
only the shape genuinely shared across every service (image defaults, ingress structure, and so on);
the per-service settings live one file per service under `values/` and `values-production/`, plus one
file per CI workflow under `image-tags/` for the tag each deploy actually pins. See
[ADR 0021](../adr/0021-kubernetes-helm-migration.md) for why it's split this way. `values-local.yaml`
layers the knobs specific to the local `kind` cluster / sandbox namespace this is actually validated
against; production is the real, currently-live deployment behind the demo links in the README, not a
placeholder. See [Backend deployment topology](../architecture/backend.md) for the full
local-vs-cluster breakdown, including why the environment is pinned to `Development` there.

This path pulls published images by the tag each service's own CI workflow last wrote to its
`image-tags/*.yaml` file — it never builds from whatever is on disk. Uncommitted local changes (or
anything on a branch CI hasn't run for yet) won't show up here even after
`scripts/k8s/up.sh`/`apply.sh`, and `pullPolicy: IfNotPresent` means a `kind` node that already
pulled a tag once won't re-pull it either. For iterating on local source — frontend or backend — use
[`scripts/all/deploy.sh`](local-development.md#running-locally) instead: it rebuilds every image from
the current working tree on every run.

## Local kind cluster setup

A local `kind` cluster needs Traefik as its ingress controller and a couple of host port mappings to
actually front it — `kind`'s own node image ships neither:

```
kind create cluster --config scripts/k8s/kind-config.yaml
scripts/k8s/install-traefik.sh
```

## Secrets

Secrets are never committed as plaintext, but which mechanism applies depends on whether the values
are throwaway or meant to persist:

For a disposable local cluster, each service still ships a plain template under
`infra/helm/gaming-backend-platform/secrets.example/` — copy, fill in local values, apply directly,
and never check the filled-in copy in:

```
cp infra/helm/gaming-backend-platform/secrets.example/identity.yaml /tmp/identity-secrets.yaml
cp infra/helm/gaming-backend-platform/secrets.example/economy.yaml /tmp/economy-secrets.yaml
cp infra/helm/gaming-backend-platform/secrets.example/rabbitmq.yaml /tmp/rabbitmq-secrets.yaml
# edit each of the three with real values, then:
kubectl create namespace gaming-platform
kubectl apply -f /tmp/identity-secrets.yaml -f /tmp/economy-secrets.yaml -f /tmp/rabbitmq-secrets.yaml
scripts/k8s/apply.sh
```

(`scripts/k8s/up.sh` already automates exactly this for the local `kind` cluster, generating fresh
values into a scratch directory on first run — nothing above is needed if you're just running the
stack locally.)

For values meant to survive a rebuild and actually be reviewable in git — the real deployment this
eventually targets — secrets are encrypted with [SOPS](https://github.com/getsops/sops) using `age`
as the encryption backend, not left as an unencrypted file someone has to remember to keep out of
version control. `.sops.yaml` at the repo root scopes which paths get encrypted and with which
recipient key:

```yaml
creation_rules:
  - path_regex: infra[\\/]helm[\\/]gaming-backend-platform[\\/]secrets\.enc[\\/].*\.enc\.yaml$
    encrypted_regex: ^(stringData|data)$
    age: age1kl06atlam4ngyp0x8h6d4hv58p7m6qv8xa6gnpewsa64srem9c2q65pvjc
```

`encrypted_regex` keeps only `stringData`'s values ciphertext — `apiVersion`, `kind`, `metadata` and
the `stringData` keys themselves stay legible, so a `git diff` on one of these files still shows
which secret changed even though the value itself doesn't. The matching private key never lives in
the repo; it sits wherever `sops` looks for it by default (`$XDG_CONFIG_HOME/sops/age/keys.txt` on
Linux/macOS, `%AppData%\sops\age\keys.txt` on Windows), generated once per operator with
`age-keygen`. Production's own keypair is generated and held the same way, outside git — the
mechanism above is exactly what the live deployment uses, not a separate one built only to
demonstrate it.

`infra/helm/gaming-backend-platform/secrets.enc/` holds the encrypted, real-value counterparts of the
five `secrets.example/` templates, committed alongside them rather than replacing them — the plain
templates remain the "here's the shape" reference for anyone who hasn't set up an age key yet.
Encrypting a filled-in template and applying it looks like:

```
sops -e -i infra/helm/gaming-backend-platform/secrets.enc/identity.enc.yaml

sops -d infra/helm/gaming-backend-platform/secrets.enc/identity.enc.yaml      | kubectl apply -f -
sops -d infra/helm/gaming-backend-platform/secrets.enc/economy.enc.yaml       | kubectl apply -f -
sops -d infra/helm/gaming-backend-platform/secrets.enc/email-service.enc.yaml | kubectl apply -f -
sops -d infra/helm/gaming-backend-platform/secrets.enc/rabbitmq.enc.yaml      | kubectl apply -f -
sops -d infra/helm/gaming-backend-platform/secrets.enc/grafana.enc.yaml       | kubectl apply -f -
scripts/k8s/apply.sh
```

Piping `sops -d` straight into `kubectl apply -f -` means the decrypted YAML never touches disk at
all; if it does for any reason (debugging a template, say), delete it once the apply succeeds rather
than leaving it next to its encrypted counterpart.

`gateway`, `economy-service` and `notification-service` validate tokens against Identity's published
JWKS rather than holding any signing secret of their own — only `identity-secrets` carries the
private key ([ADR 0017](../adr/0017-rs256-and-jwks.md)). Consul is not deployed at all here —
Kubernetes Services and kube-DNS already provide discovery
([ADR 0002](../adr/0002-api-gateway-ocelot-consul.md)).

## Apply mechanics

`scripts/k8s/apply.sh` is a thin `helm upgrade --install` wrapper, not a hand-rolled apply order: the
two database StatefulSets and the `identity-migrator`/`economy-migrator` Jobs are
`pre-install`/`pre-upgrade` Helm hooks (see `templates/statefulset.yaml` and
`templates/migration-job.yaml` in the chart), so Helm itself finishes them before any app Deployment —
including `mailpit`, `player-client` and `admin-client` — gets created. A `Job` still has no
`depends_on: condition: service_completed_successfully` equivalent, but this is Helm's own mechanism
for exactly that problem rather than a wrapper script re-implementing `kubectl wait` by hand. The
chart's `gateway-config` ConfigMap is generated the same way: straight from
`backend/ApiGateway/ocelot.Kubernetes.json`, which lives outside the chart, so `apply.sh` passes it
in with `--set-file` rather than keeping a second copy that could drift.

## Reaching the stack

Reach the stack through the Ingress, fronted by Traefik: every web-facing service gets its own
single-level `*.localhost` hostname, which every major browser/OS resolves to `127.0.0.1` with zero
setup (RFC 6761) — no `/etc/hosts` entry needed, unlike a path-plus-one-host layout.

| Host | Routes to |
|---|---|
| `player-client.localhost` | `player-client` (itself proxying `/api` onward to the gateway) |
| `admin-client.localhost` | `admin-client` |
| `mailpit.localhost` | Mailpit's UI (kind/sandbox only) |
| `gateway.localhost` | `api-gateway` directly — convenient for Postman/curl against the API, not something the web clients themselves need |
| `traefik.localhost` | Traefik's own dashboard (`/dashboard/`, trailing slash required) — routers, services and middleware, live |

Seeded demo accounts, all sharing the password `DemoPassword123!` (`DevelopmentSeeder`, this password
only exists on a local/sandbox cluster — never a real deployment):

| Email | Role |
|---|---|
| `admin@demo-shooter.dev` | Platform admin |
| `player.one@demo-shooter.dev`, `player.two@demo-shooter.dev` | Players, `demo-shooter` |
| `gameadmin@demo-racer.dev` | Game admin, `demo-racer` |
| `player.three@demo-racer.dev` | Player, `demo-racer` |

See [Backend deployment topology](../architecture/backend.md) for why player-client's own Nginx does
the `/api` proxying rather than a second Ingress rule. Or port-forward directly:

```
kubectl -n gaming-platform port-forward svc/player-client 8080:8080
kubectl -n gaming-platform port-forward svc/admin-client 8081:8081
kubectl -n gaming-platform port-forward svc/api-gateway 5100:5100
kubectl -n gaming-platform port-forward svc/mailpit 8025:8025   # kind/sandbox only
```

## Related documentation

- [ADR 0021: Kubernetes/Helm migration](../adr/0021-kubernetes-helm-migration.md)
- [ADR 0002: API Gateway (Ocelot + Consul)](../adr/0002-api-gateway-ocelot-consul.md)
- [Backend deployment topology](../architecture/backend.md)
- [GitOps and CI/CD](gitops.md)
- [Local development](local-development.md)
