using IdentityService.Contracts.Requests;
using IdentityService.Contracts.Responses;
using IdentityService.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace IdentityService.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity/auth");

        group.MapPost("/register", RegisterAsync);
    }

    private static async Task<Accepted<RegistrationAcceptedResponse>> RegisterAsync(
        RegisterRequest request,
        IAuthenticationService authenticationService,
        CancellationToken cancellationToken)
    {
        var result = await authenticationService.RegisterAsync(
            request.GameSlug, request.Email, request.DisplayName, request.Password, cancellationToken);

        var response = new RegistrationAcceptedResponse(
            result.UserId, result.Email, result.VerificationRequired, result.CodeExpiresAt);

        return TypedResults.Accepted((string?)null, response);
    }
}
