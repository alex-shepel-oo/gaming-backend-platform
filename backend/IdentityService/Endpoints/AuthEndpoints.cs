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
        group.MapPost("/confirm-email", ConfirmEmailAsync);
        group.MapPost("/resend-verification", ResendVerificationAsync);
        group.MapPost("/login", LoginAsync);
        group.MapPost("/refresh", RefreshAsync);
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

    private static async Task<NoContent> ConfirmEmailAsync(
        ConfirmEmailRequest request,
        IEmailVerificationService emailVerificationService,
        CancellationToken cancellationToken)
    {
        await emailVerificationService.ConfirmAsync(request.Email, request.Code, cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Accepted> ResendVerificationAsync(
        ResendVerificationRequest request,
        IEmailVerificationService emailVerificationService,
        CancellationToken cancellationToken)
    {
        await emailVerificationService.ResendAsync(request.Email, request.GameSlug, cancellationToken);

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Ok<TokenPairResponse>> LoginAsync(
        LoginRequest request,
        IAuthenticationService authenticationService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();

        var result = await authenticationService.LoginAsync(
            request.GameSlug, request.Email, request.Password, ip, userAgent, cancellationToken);

        return TypedResults.Ok(new TokenPairResponse(result.AccessToken, result.RefreshToken));
    }

    private static async Task<Ok<TokenPairResponse>> RefreshAsync(
        RefreshRequest request,
        IRefreshTokenService refreshTokenService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString();

        var result = await refreshTokenService.RotateAsync(request.RefreshToken, ip, cancellationToken);

        return TypedResults.Ok(new TokenPairResponse(result.AccessToken, result.RawRefreshToken));
    }
}
