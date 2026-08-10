# Local development

## Running locally

```
scripts/all/deploy.sh
```

One command: `scripts/all/setup-env.sh` creates `infra/.env` from the example first (generating a
real local JWT signing key if it's still the placeholder), then brings up the whole stack — both
Postgres instances, Consul, RabbitMQ, Mailpit, IdentityService, EconomyService, Platform.Worker,
ApiGateway, player-client and admin-client. See [Local automation](#local-automation) below for the
rest of `scripts/` — this is one entry point among several, not the only thing in there.

Or the same result spelled out manually, without the script:

```
cp infra/.env.example infra/.env
cd infra
docker compose up
```

The player browser client is at `http://localhost:8080`, the admin one at `http://localhost:8081`;
anything hitting the API directly goes through the gateway at `http://localhost:5100`. Mailpit's UI
(for reading verification emails without a real mailbox) is at `http://localhost:8025`.

Almost every value in `infra/.env.example` is committed on purpose and isn't a production secret: the
stack only binds to `localhost`, so nothing in it is reachable from outside the machine it runs on,
and every clone gets its own `.env` by copying the example rather than sharing one committed file.
The one exception is `Jwt__PrivateKeyPem` (the RSA key IdentityService signs tokens with,
[ADR 0017](../adr/0017-rs256-and-jwks.md)) — that one is deliberately left as a placeholder, not a
working key, since real RSA key material is worth committing even less than an arbitrary dummy
string. Generate your own before the first `docker compose up`; the comment above that line in
`.env.example` has the one-liner.

## Local automation

`scripts/` mirrors the CI steps in [GitOps and CI/CD](gitops.md) for local use — each script calls
the same commands its corresponding workflow does, rather than a parallel set of commands that could
quietly drift from what CI actually checks.

```
scripts/
├── backend/
│   ├── build.sh    # dotnet build backend/GamingBackendPlatform.slnx
│   ├── test.sh     # dotnet test backend/GamingBackendPlatform.slnx
│   └── deploy.sh   # docker compose up -d, every backend service, no frontend
├── frontend/
│   ├── build.sh    # npm ci && npm run build (shared, then player-client, then admin-client)
│   ├── test.sh     # npm run test (Vitest, all three projects)
│   └── deploy.sh   # docker compose up -d player-client admin-client
├── all/
│   ├── setup-env.sh # idempotent: creates infra/.env from the example and fills in
│   │                 # a real local signing key if it's still the placeholder --
│   │                 # every deploy.sh below calls this first
│   ├── verify.sh   # backend build+test, then frontend build+test, no deploy
│   ├── deploy.sh   # docker compose up -d, the whole stack
│   ├── ci.sh       # verify.sh, then deploy.sh
│   └── stop.sh     # docker compose down (--clean also drops volumes and prunes images)
└── k8s/
    ├── kind-config.yaml           # kind cluster config: host port mappings for Traefik
    ├── install-traefik.sh         # one-time cluster addon install
    ├── traefik-values-local.yaml  # values for the official Traefik chart on kind
    ├── apply.sh                   # helm upgrade --install the chart
    └── teardown.sh                # kind delete cluster --name gbp
```

**`verify.sh` vs `ci.sh`:** `verify.sh` is the quick local gate — build and test both sides and stop
there, nothing gets deployed afterward. `ci.sh` is `verify.sh` plus a full-stack deploy at the end,
for when the goal is a running stack, not just confirmation that everything builds and passes.

## Related documentation

- [Deployment](deployment.md)
- [GitOps and CI/CD](gitops.md)
- [ADR 0017: RS256 + JWKS](../adr/0017-rs256-and-jwks.md)
