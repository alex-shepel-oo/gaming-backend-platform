# ApiGateway

## Responsibility

Single entry point for both frontend apps. Routes by upstream path template to the owning backend
service, enforces the audience/scope claim checks that keep player-facing and admin-facing traffic
separated before either reaches a backend service, and terminates two distinct CORS policies.

## Architecture

Ocelot ([ADR 0002](../adr/0002-api-gateway-ocelot-consul.md)), configuration-driven rather than
code-routed: `ocelot.json` defines every route (upstream/downstream path templates, allowed HTTP
methods, `RouteClaimsRequirement` for `scope`/`aud`), layered with an environment file
(`ocelot.Development.json` for Consul `ServiceName` resolution, `ocelot.Kubernetes.json` for static
`DownstreamHostAndPorts`) that supplies only the service-address half — merged **positionally by
array index**, not by route path, which is the one real footgun in this setup: adding a new route to
`ocelot.json` without a matching same-index placeholder in both environment files desyncs every route
after it. `OcelotConfigurationTests` catches this class of mistake before it reaches a running gateway.

Two CORS policies, not one shared whitelist — `PlayerClientCorsPolicy` and `AdminClientCorsPolicy`,
each with `AllowCredentials=true`, picked by two explicit `UseWhen` branches ahead of `UseOcelot()`
(Ocelot has no per-route CORS config of its own) rather than one blanket `UseCors` call. The built
demo images don't need either policy in practice — each app's own Nginx proxies `/api` same-origin —
CORS only matters for `ng serve` and any future direct client. See
[ADR 0016](../adr/0016-admin-surface-isolation.md).

## API

Not an API of its own — a routing layer in front of IdentityService and EconomyService's real APIs.
See [Identity API](../api/identity.md) and [Economy API](../api/economy.md) for what actually sits
behind it.

## Data

None. Stateless.

## Messaging

None. Purely synchronous HTTP routing.

## Dependencies

Every backend service it routes to (IdentityService, EconomyService, NotificationService's `/hubs` is
the one path deliberately routed *around* it — see [Frontend architecture](../architecture/frontend.md)
for why). Consul for service discovery locally; nothing for discovery in Kubernetes, where kube-DNS
already resolves the names `ocelot.Kubernetes.json` hardcodes.

## Security

`RouteClaimsRequirement` on admin-facing routes checks `aud=gbp-admin`/`scope=Platform` (or
game-scoped variants) before Ocelot even proxies the request — a routing-layer check, not a
replacement for the same check IdentityService/EconomyService make themselves on the claims in the
validated JWT. `GlobalExceptionHandler` maps unhandled exceptions to `ProblemDetails` rather than
leaking a stack trace. Full detail: [Security overview](../security/overview.md).

## Deployment

Docker image, stateless Deployment, no PVC, no database. See
[Backend deployment topology](../architecture/backend.md).

## Observability

OpenTelemetry traces/metrics via `BuildingBlocks.Telemetry` — every request through the gateway is the
first hop in a trace that continues into whichever backend service handled it. `/health` endpoint.

## Related documentation

- [ADR 0002: API Gateway (Ocelot + Consul)](../adr/0002-api-gateway-ocelot-consul.md)
- [ADR 0016: Admin surface isolation](../adr/0016-admin-surface-isolation.md)
- [Frontend architecture](../architecture/frontend.md)
- [Backend deployment topology](../architecture/backend.md)
