using EconomyService.Auth;
using EconomyService.Contracts.Requests;
using EconomyService.Contracts.Responses;
using EconomyService.Domain;
using EconomyService.Exceptions;
using EconomyService.Persistence;
using EconomyService.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EconomyService.Endpoints;

public static class ConversionEndpoints
{
    public static void MapConversionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/conversions");

        group.MapPost("", CreateAsync).RequireAuthorization();
        group.MapGet("/{id:guid}", GetAsync).RequireAuthorization();
        group.MapGet("/rate", GetRateAsync).RequireAuthorization();
        group.MapPost("/{id:guid}/cancel", CancelAsync).RequireAuthorization();
    }

    private static async Task<Accepted<ConversionDto>> CreateAsync(
        ConvertRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ICurrentUser currentUser,
        IConversionRequestService conversionRequestService,
        CancellationToken cancellationToken)
    {
        var conversionId = RequireConversionId(idempotencyKey);

        var result = await conversionRequestService.CreateOrGetAsync(
            new ConversionCreationRequest(
                conversionId, currentUser.UserId, request.FromCurrencyId, request.ToCurrencyId, request.FromAmount),
            cancellationToken);

        return TypedResults.Accepted($"/conversions/{result.Request.Id}", ToDto(result.Request));
    }

    private static async Task<Results<Ok<ConversionDto>, NotFound>> GetAsync(
        Guid id,
        ICurrentUser currentUser,
        EconomyDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // Owner-scoped: a conversion that exists but belongs to someone else
        // returns the same 404 as one that doesn't exist at all, rather than
        // 403, so the response never confirms another user's conversion id.
        var request = await dbContext.ConversionRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id && r.UserId == currentUser.UserId, cancellationToken);

        return request is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ToDto(request));
    }

    // The Idempotency-Key doubles as the conversion's id, so it must
    // parse as a GUID - missing and malformed both fall into the same 400 bucket.
    private static Guid RequireConversionId(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || !Guid.TryParse(idempotencyKey, out var conversionId))
        {
            throw new MissingIdempotencyKeyException();
        }

        return conversionId;
    }

    private static async Task<Ok<ConversionRateDto>> GetRateAsync(
        Guid fromCurrencyId,
        Guid toCurrencyId,
        IConversionRequestService conversionRequestService,
        CancellationToken cancellationToken)
    {
        var rate = await conversionRequestService.GetRateAsync(fromCurrencyId, toCurrencyId, cancellationToken);
        return TypedResults.Ok(new ConversionRateDto(rate.FromCurrencyId, rate.ToCurrencyId, rate.Rate));
    }

    private static async Task<Results<Ok<ConversionDto>, NotFound>> CancelAsync(
        Guid id,
        ICurrentUser currentUser,
        IConversionSaga conversionSaga,
        EconomyDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // Same owner-scoped 404 as GetAsync - a conversion belonging to
        // someone else must not be distinguishable from one that doesn't
        // exist at all.
        var owned = await dbContext.ConversionRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id && r.UserId == currentUser.UserId, cancellationToken);

        if (owned is null)
        {
            return TypedResults.NotFound();
        }

        await conversionSaga.TryCancelAsync(id, cancellationToken);

        var updated = await dbContext.ConversionRequests
            .AsNoTracking()
            .SingleAsync(r => r.Id == id, cancellationToken);

        return TypedResults.Ok(ToDto(updated));
    }

    private static ConversionDto ToDto(ConversionRequest request) => new(
        request.Id,
        request.UserId,
        request.FromCurrencyId,
        request.ToCurrencyId,
        request.FromAmount,
        request.ToAmount,
        request.RateApplied,
        request.Status,
        request.FailureReason,
        request.CreatedAt,
        request.UpdatedAt);
}
