using EconomyService.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace EconomyService.Infrastructure;

public sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title, detail) = exception switch
        {
            MissingIdempotencyKeyException => (StatusCodes.Status400BadRequest, "Idempotency-Key required", exception.Message),
            UnsupportedConversionPairException => (StatusCodes.Status400BadRequest, "Unsupported conversion pair", exception.Message),
            ConversionNotCancellableException => (StatusCodes.Status409Conflict, "Conversion cannot be cancelled", exception.Message),
            InsufficientFundsException => (StatusCodes.Status402PaymentRequired, "Insufficient funds", exception.Message),
            IdempotencyKeyConflictException => (StatusCodes.Status409Conflict, "Idempotency-Key conflict", exception.Message),
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
