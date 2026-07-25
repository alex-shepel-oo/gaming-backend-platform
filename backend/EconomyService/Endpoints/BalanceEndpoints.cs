using EconomyService.Auth;
using EconomyService.Contracts.Requests;
using EconomyService.Contracts.Responses;
using EconomyService.Persistence;
using EconomyService.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EconomyService.Endpoints;

public static class BalanceEndpoints
{
    public static void MapBalanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/balances");

        group.MapGet("/me", GetMyBalancesAsync).RequireAuthorization();
        group.MapPost("/{userId:guid}/adjust", AdjustAsync).RequireAuthorization(policy => policy.RequireClaim(
            EconomyClaims.Perms, Permissions.GameBalanceAdjust, Permissions.PlatformBalanceAdjust));
    }

    private static async Task<Results<Ok<BalanceDto[]>, ForbidHttpResult>> GetMyBalancesAsync(
        Guid? gameId,
        ICurrentUser currentUser,
        IBalanceService balanceService,
        CancellationToken cancellationToken)
    {
        // gameId is an optional cross-check, not a filter switch: a caller can only
        // ever ask about the game their own token is scoped to. A mismatch here is
        // a tenant-isolation violation, not a "no results" case.
        if (gameId is not null && gameId != currentUser.GameId)
        {
            return TypedResults.Forbid();
        }

        var balances = await balanceService.GetBalancesForUserAsync(currentUser.UserId, currentUser.GameId, cancellationToken);

        var response = balances
            .OrderBy(b => b.Currency!.Code)
            .Select(b => new BalanceDto(b.CurrencyId, b.Currency!.Code, b.Currency.Scope, b.Currency.GameId, b.Amount))
            .ToArray();

        return TypedResults.Ok(response);
    }

    private static async Task<Results<Created<TransactionDto>, Ok<TransactionDto>, ForbidHttpResult>> AdjustAsync(
        Guid userId,
        AdjustRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ICurrentUser currentUser,
        EconomyDbContext dbContext,
        ILedgerService ledgerService,
        CancellationToken cancellationToken)
    {
        LedgerResultMapping.RequireIdempotencyKey(idempotencyKey);

        var targetGameId = await dbContext.Currencies
            .Where(c => c.Id == request.CurrencyId)
            .Select(c => c.GameId)
            .SingleAsync(cancellationToken);

        if (!BalanceScopeGuard.CanAdjust(currentUser, targetGameId))
        {
            return TypedResults.Forbid();
        }

        var result = await ledgerService.AdjustAsync(
            new LedgerMutationRequest(userId, request.CurrencyId, request.Amount, idempotencyKey!, request.Reason),
            cancellationToken);

        var dto = LedgerResultMapping.ToDto(result);
        return result.IsReplay ? TypedResults.Ok(dto) : TypedResults.Created((string?)null, dto);
    }
}
