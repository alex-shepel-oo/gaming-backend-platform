using IdentityService.Auth;
using IdentityService.Contracts.Requests;
using IdentityService.Contracts.Responses;
using IdentityService.RateLimiting;
using IdentityService.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;

namespace IdentityService.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/identity/auth");

        group.MapPost("/register", RegisterAsync).RequireRateLimiting(RateLimitPolicies.Register);
        group.MapPost("/confirm-email", ConfirmEmailAsync).RequireRateLimiting(RateLimitPolicies.ConfirmEmail);
        group.MapPost("/resend-verification", ResendVerificationAsync).RequireRateLimiting(RateLimitPolicies.ResendVerification);
        group.MapPost("/request-password-reset", RequestPasswordResetAsync).RequireRateLimiting(RateLimitPolicies.RequestPasswordReset);
        group.MapPost("/login", LoginAsync).RequireRateLimiting(RateLimitPolicies.Login);
        group.MapPost("/refresh", RefreshAsync);
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();
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

    private static async Task<Accepted> RequestPasswordResetAsync(
        RequestPasswordResetRequest request,
        IPasswordResetService passwordResetService,
        CancellationToken cancellationToken)
    {
        await passwordResetService.RequestResetAsync(request.Email, cancellationToken);

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<Ok<TokenPairResponse>, Ok<AccessTokenResponse>>> LoginAsync(
        LoginRequest request,
        IAuthenticationService authenticationService,
        ICookieAuthWriter cookieAuthWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext.Request.Headers.UserAgent.ToString();

        var result = await authenticationService.LoginAsync(
            request.GameSlug, request.Email, request.Password, ip, userAgent, cancellationToken);

        if (ClientMode.IsWeb(httpContext))
        {
            cookieAuthWriter.WriteRefresh(httpContext.Response, result.RefreshToken);

            return TypedResults.Ok(new AccessTokenResponse(result.AccessToken));
        }

        return TypedResults.Ok(new TokenPairResponse(result.AccessToken, result.RefreshToken));
    }

    private static async Task<Results<Ok<TokenPairResponse>, Ok<AccessTokenResponse>>> RefreshAsync(
        RefreshRequest? request,
        IRefreshTokenService refreshTokenService,
        ICookieAuthWriter cookieAuthWriter,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString();
        var isWeb = ClientMode.IsWeb(httpContext);

        var rawToken = (isWeb ? cookieAuthWriter.ReadRefresh(httpContext.Request) : request?.RefreshToken) ?? string.Empty;

        var result = await refreshTokenService.RotateAsync(rawToken, ip, cancellationToken);

        if (isWeb)
        {
            cookieAuthWriter.WriteRefresh(httpContext.Response, result.RawRefreshToken);

            return TypedResults.Ok(new AccessTokenResponse(result.AccessToken));
        }

        return TypedResults.Ok(new TokenPairResponse(result.AccessToken, result.RawRefreshToken));
    }

    private static async Task<NoContent> LogoutAsync(
        LogoutRequest? request,
        ISessionService sessionService,
        ICookieAuthWriter cookieAuthWriter,
        ICurrentUser currentUser,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var isWeb = ClientMode.IsWeb(httpContext);

        var rawToken = (isWeb ? cookieAuthWriter.ReadRefresh(httpContext.Request) : request?.RefreshToken) ?? string.Empty;

        await sessionService.LogoutAsync(
            currentUser.UserId,
            currentUser.GameId,
            currentUser.Jti,
            currentUser.ExpiresAt,
            rawToken,
            cancellationToken);

        if (isWeb)
        {
            cookieAuthWriter.ClearRefresh(httpContext.Response);
        }

        return TypedResults.NoContent();
    }
}
