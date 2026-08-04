using IdentityService.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace IdentityService.Infrastructure;

public sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title, detail, type) = exception switch
        {
            InvalidCredentialsException => (StatusCodes.Status401Unauthorized, "Invalid credentials", exception.Message, null),
            InvalidRefreshTokenException => (StatusCodes.Status401Unauthorized, "Invalid refresh token", exception.Message, null),
            GameNotFoundException => (StatusCodes.Status404NotFound, "Game not found", exception.Message, null),
            GameSlugAlreadyExistsException => (StatusCodes.Status409Conflict, "Game slug already exists", exception.Message, null),
            EmailAlreadyExistsException => (StatusCodes.Status409Conflict, "Email already exists", exception.Message, null),
            InvalidVerificationCodeException => (StatusCodes.Status400BadRequest, "Invalid verification code", exception.Message, null),
            InvalidPasswordResetTokenException => (StatusCodes.Status400BadRequest, "Invalid password reset token", exception.Message, null),
            InvalidAvatarUrlException => (StatusCodes.Status400BadRequest, "Invalid avatar URL", exception.Message, null),
            InvalidIconUrlException => (StatusCodes.Status400BadRequest, "Invalid icon URL", exception.Message, null),
            AccountDisabledException => (StatusCodes.Status403Forbidden, "Account disabled", exception.Message, null),
            EmailNotConfirmedException => (StatusCodes.Status403Forbidden, "Email not confirmed", exception.Message,
                "https://gaming-backend-platform/problems/email-not-confirmed"),
            NoAccessToGameException => (StatusCodes.Status403Forbidden, "No access to game", exception.Message, null),
            PermissionEscalationException => (StatusCodes.Status403Forbidden, "Permission escalation", exception.Message, null),
            RefreshTokenOwnerMismatchException => (StatusCodes.Status403Forbidden, "Refresh token owner mismatch", exception.Message, null),
            UserNotFoundException => (StatusCodes.Status404NotFound, "User not found", exception.Message, null),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred",
                "An unexpected error occurred while processing the request.", null),
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            LogUnhandledException(exception);
        }

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
                Type = type,
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private partial void LogUnhandledException(Exception exception);
}
