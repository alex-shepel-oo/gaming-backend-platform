using EconomyService.Auth;
using EconomyService.Contracts.Requests;
using EconomyService.Contracts.Responses;
using EconomyService.Exceptions;
using EconomyService.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace EconomyService.Endpoints;

public static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/transactions");

        group.MapPost("/grant", GrantAsync).RequireAuthorization(Policies.ModeratorOrAbove);
        group.MapPost("/spend", SpendAsync).RequireAuthorization();
    }

    private static async Task<Results<Created<TransactionDto>, Ok<TransactionDto>>> GrantAsync(
        GrantRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ILedgerService ledgerService,
        CancellationToken cancellationToken)
    {
        RequireIdempotencyKey(idempotencyKey);

        var result = await ledgerService.GrantAsync(
            new LedgerMutationRequest(request.UserId, request.CurrencyId, request.Amount, idempotencyKey!, request.Reason),
            cancellationToken);

        return ToResult(result);
    }

    private static async Task<Results<Created<TransactionDto>, Ok<TransactionDto>>> SpendAsync(
        SpendRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ICurrentUser currentUser,
        ILedgerService ledgerService,
        CancellationToken cancellationToken)
    {
        RequireIdempotencyKey(idempotencyKey);

        var result = await ledgerService.SpendAsync(
            new LedgerMutationRequest(currentUser.UserId, request.CurrencyId, request.Amount, idempotencyKey!, request.Reason),
            cancellationToken);

        return ToResult(result);
    }

    private static void RequireIdempotencyKey(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new MissingIdempotencyKeyException();
        }
    }

    // A replayed idempotency key returns the outcome of the original mutation,
    // not a new one - 200 tells the caller nothing was created this time, 201
    // that it was (A.4).
    private static Results<Created<TransactionDto>, Ok<TransactionDto>> ToResult(LedgerPostResult result)
    {
        var dto = new TransactionDto(
            result.Entry.Id,
            result.Entry.UserId,
            result.Entry.CurrencyId,
            result.Entry.Amount,
            result.Entry.TransactionType,
            result.Entry.Reason,
            result.Balance,
            result.Entry.CreatedAt);

        return result.IsReplay ? TypedResults.Ok(dto) : TypedResults.Created((string?)null, dto);
    }
}
