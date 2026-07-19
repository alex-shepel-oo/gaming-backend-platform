using EconomyService.Auth;
using EconomyService.Contracts.Requests;
using EconomyService.Contracts.Responses;
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
        LedgerResultMapping.RequireIdempotencyKey(idempotencyKey);

        var result = await ledgerService.GrantAsync(
            new LedgerMutationRequest(request.UserId, request.CurrencyId, request.Amount, idempotencyKey!, request.Reason),
            cancellationToken);

        return LedgerResultMapping.ToTransactionResult(result);
    }

    private static async Task<Results<Created<TransactionDto>, Ok<TransactionDto>>> SpendAsync(
        SpendRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ICurrentUser currentUser,
        ILedgerService ledgerService,
        CancellationToken cancellationToken)
    {
        LedgerResultMapping.RequireIdempotencyKey(idempotencyKey);

        var result = await ledgerService.SpendAsync(
            new LedgerMutationRequest(currentUser.UserId, request.CurrencyId, request.Amount, idempotencyKey!, request.Reason),
            cancellationToken);

        return LedgerResultMapping.ToTransactionResult(result);
    }
}
