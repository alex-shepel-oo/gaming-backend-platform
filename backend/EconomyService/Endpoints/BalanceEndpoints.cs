using EconomyService.Auth;
using EconomyService.Contracts.Responses;
using EconomyService.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EconomyService.Endpoints;

public static class BalanceEndpoints
{
    public static void MapBalanceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/balances/me", GetMyBalancesAsync).RequireAuthorization();
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
}
