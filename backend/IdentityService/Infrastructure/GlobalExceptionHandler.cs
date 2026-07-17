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
        var (statusCode, title, detail) = exception switch
        {
            InvalidCredentialsException => (StatusCodes.Status401Unauthorized, "Invalid credentials", exception.Message),
            InvalidRefreshTokenException => (StatusCodes.Status401Unauthorized, "Invalid refresh token", exception.Message),
            GameNotFoundException => (StatusCodes.Status404NotFound, "Game not found", exception.Message),
            EmailAlreadyExistsException => (StatusCodes.Status409Conflict, "Email already exists", exception.Message),
            InvalidVerificationCodeException => (StatusCodes.Status400BadRequest, "Invalid verification code", exception.Message),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred",
                "An unexpected error occurred while processing the request."),
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
            },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception")]
    private partial void LogUnhandledException(Exception exception);
}
