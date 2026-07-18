# ADR-0002: API Gateway — Ocelot + Consul

- **Status:** Accepted
- **Date:** 2026-07-16

## Context

A single entry point is needed to route to several independent services, to enforce
authentication/authorization uniformly at the system boundary, and to aggregate API
documentation. This required choosing both the gateway tool itself and the service discovery
strategy for two genuinely different environments: local docker-compose (no discovery built in)
and Kubernetes (kube-DNS out of the box).

Two decisions in this ADR are not obvious from the code that follows and need to be recorded
explicitly.

## Decision

**Gateway: Ocelot 24.1.0 on .NET 10.** Ocelot stays on its latest stable release (24.1.0,
targeting `net8.0`/`net9.0`) while the services themselves target `net10.0`. Ocelot's own .NET 10
release (25.0) exists only as a pre-release at the time of this decision (latest:
`25.0.0-beta.3`). A `net8.0` package consumed by a `net10.0` project is standard TFM
compatibility, not a workaround. A stable dependency reads better in a portfolio repository than
a beta one, and this will be revisited once Ocelot 25.x reaches general availability.

**Service discovery: Consul only where the environment has none of its own.** In docker-compose,
services register with Consul and Ocelot resolves routes through it (`ServiceDiscoveryProvider:
Consul` + `ServiceName` per route). In Kubernetes, routes resolve through static
`DownstreamHostAndPorts` pointing at cluster DNS names — no Consul is deployed there, because
Kubernetes Services and kube-DNS already provide discovery, and running Consul alongside them
would mean two systems answering the same question. This is implemented as environment-specific
Ocelot configuration files (`ocelot.json` base + `ocelot.Development.json` +
`ocelot.Kubernetes.json`), selected by `ASPNETCORE_ENVIRONMENT`.

Resilience on downstream calls (timeout, retry, circuit breaker) uses `Ocelot.Provider.Polly`
with the current, non-deprecated option names (`Timeout`, `FailureRatio`, `SamplingDuration`) —
not the options deprecated as of Ocelot 24.1 (`DurationOfBreak`,
`ExceptionsAllowedBeforeBreaking`, `TimeoutValue`), which most existing examples online still
show.

Authorization policies (Player / Moderator / Admin, from JWT claims) are enforced both at the
gateway and inside each downstream service. The gateway's check is a coarse, early rejection; the
service's check is the authority. This is defence in depth, not duplicated logic left in by
oversight.

**API documentation is aggregated by proxying, not by a Swagger-aggregation package.** The usual
tool for this with Ocelot is `MMLib.SwaggerForOcelot`, and version 10.0.2 does target `net10.0`
and work with Ocelot >= 24.1.0 — it would technically fit. It is not used: it pulls in
`Swashbuckle.AspNetCore.SwaggerGen`/`SwaggerUI` 10.0.1 transitively, the exact package family the
services deliberately left behind in favour of `Microsoft.AspNetCore.OpenApi` and Scalar, and it
would leave the gateway as the one place in the system still rendering a Swashbuckle UI. Its main
feature — rewriting downstream paths into upstream ones — is also a no-op here, since every route
in this ADR already keeps its upstream path identical to its downstream path. Instead, the gateway
proxies `/openapi/identity/v1.json` to identity-service's own `/openapi/v1.json` through an
ordinary route (`ServiceName` in development, a static host in Kubernetes, exactly like any other
route), and Scalar on the gateway points at that document as its source.

## Alternatives considered

| Option | Why not |
|---|---|
| Ocelot 25.0.0-beta on net10.0 | Removes the TFM-compatibility note, but ships a beta dependency in a portfolio repository; revisit once 25.x is GA |
| YARP as the gateway | A credible alternative and arguably more "native" to .NET, but Ocelot's file-based routing config and built-in QoS/Consul providers fit this project's scope with less custom code; YARP would mean writing more of that glue by hand |
| Consul in Kubernetes as well, for consistency across environments | Rejected — it duplicates a responsibility Kubernetes already provides, and a reviewer would reasonably ask why two discovery systems exist side by side for no functional gain |
| Trusting only the gateway's authorization check | Rejected — a downstream service that trusted the gateway would be one routing mistake away from being open to anything that reached it by another path |
| `MMLib.SwaggerForOcelot` for aggregated Swagger UI | Technically compatible (10.0.2, `net10.0`, Ocelot >= 24.1.0), but reintroduces Swashbuckle transitively and its path-rewriting feature is unused here, since upstream and downstream paths already match on every route |

## Consequences

### What we get

One entry point for routing, authentication, and authorization across all services. Local
development gets a realistic demonstration of service discovery (Consul), while Kubernetes stays
free of a redundant discovery layer. Resilience policies are centralized at the boundary rather
than reimplemented per service.

### What it costs

Two Ocelot configuration files to keep in sync conceptually (even though their content differs by
design) for every new route added by a later service. A stable-but-not-latest gateway dependency
that will need a deliberate upgrade once Ocelot 25.x is GA. Authorization logic exists in two
places (gateway and service) by design, which must not be allowed to drift into two different
sources of truth over time.

### When this gets revisited

When Ocelot ships a stable `net10.0` release, or when the project's discovery needs outgrow what
Consul (in compose) and kube-DNS (in Kubernetes) provide — for example, if cross-cluster or
multi-region discovery becomes necessary. Also worth another look if a later service's upstream
path on the gateway ever diverges from its downstream path: at that point, proxying the OpenAPI
document as-is stops being a no-op, and `MMLib.SwaggerForOcelot`'s path rewriting starts earning
its keep.