# ADR-0007: Minimal APIs over controllers

- **Status:** Accepted
- **Date:** 2026-07-17

## Context

The HTTP layer for each backend service needs a concrete style: MVC controllers or Minimal APIs.
Microsoft's current guidance for ASP.NET Core recommends Minimal APIs as the default for new
projects — less code and less ceremony per endpoint — while controllers remain fully supported
and are still the better fit for large APIs with many actions per resource. Each service in this
platform exposes on the order of ten endpoints, which sits squarely in the case Minimal APIs was
designed for.

Choosing this also has a direct consequence for the existing AuthService test suite being
evolved into IdentityService: the existing `AuthControllerTests` instantiate a controller
directly with a mocked service. Minimal APIs have no controller object to instantiate, so that
test shape cannot be carried forward unchanged, and this ADR needs to say what replaces it.

## Decision

IdentityService (and every backend service after it) is built with Minimal APIs, not MVC
controllers. Concretely:

- **Route groups** — one static class per resource (`AuthEndpoints`, `UserEndpoints`,
  `GameEndpoints`), each exposing a `Map*Endpoints(this IEndpointRouteBuilder)` extension method,
  registered once from `Program.cs`. Groups carry shared tags, policies, and filters.
- **TypedResults** — endpoint handlers return `Results<Ok<T>, ProblemHttpResult>` and similar
  typed unions, not `Task<IActionResult>` with anonymous objects. In .NET 10 the response shapes
  are inferred into OpenAPI automatically from these types.
- **Built-in validation** — `AddValidation()` plus DataAnnotations on request records, source
  generated via the interceptors namespace. No FluentValidation dependency.
- **ProblemDetails (RFC 9457)** — `AddProblemDetails()` plus a single `IExceptionHandler` that
  maps domain exceptions to status codes. No `catch (Exception ex) { return
  BadRequest(ex.Message); }` anywhere in an endpoint body.
- **IOptions with `ValidateOnStart()`** for configuration (JWT signing key, email settings, etc.)
  instead of indexing `IConfiguration` inline.
- Endpoint bodies stay thin — logic lives in the service layer (`Services/`), not in the HTTP
  layer. If a handler grows past roughly ten lines, that is a sign logic has leaked into it.

**Consequence for tests:** the service layer (`TokenService`, `RefreshTokenService`,
`AuthenticationService`, etc.) is what the previous `AuthServiceImplTests` evolve into, keeping
both the approach and xUnit. The HTTP layer is tested through `WebApplicationFactory` integration
tests instead of direct controller instantiation — which, for Minimal APIs, is the standard way
to test the pipeline and is arguably a better test than the one it replaces: it exercises
authentication, validation, and `ProblemDetails` mapping as they actually run, not just a method
call in isolation.

## Alternatives considered

| Option | Why not |
|---|---|
| Keep MVC controllers, evolve `AuthControllerTests` as-is | Matches the current test shape with the least short-term friction, but goes against Microsoft's own guidance for new services this size, and controllers add filter/attribute ceremony that route groups already cover more simply |
| Minimal APIs, but organize handlers as instance classes mimicking controllers | Gets Minimal APIs' runtime benefits without its simplicity; static route-group extension methods are less code and read closer to what the framework actually expects |
| FluentValidation instead of built-in DataAnnotations validation | Works, but adds a dependency and a second validation pipeline where .NET 10's source-generated validation already covers the request shapes this service needs |

## Consequences

### What we get

Less code per endpoint, response shapes documented automatically in OpenAPI, one consistent error
format across the whole service, and configuration that fails fast at startup instead of at the
first request that touches a missing key.

### What it costs

The existing HTTP-layer tests do not carry over; they are rewritten as integration tests against
`WebApplicationFactory`, which is a real one-time cost paid in the commit that introduces each
endpoint (see the implementation plan's commit boundaries).

### When this gets revisited

If any service in the platform grows to the point where the number of endpoints per resource, or
the amount of shared per-action behavior, makes controllers genuinely simpler — that has not
happened for any service planned so far.