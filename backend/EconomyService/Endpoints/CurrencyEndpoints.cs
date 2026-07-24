using EconomyService.Auth;
using EconomyService.Contracts.Responses;
using EconomyService.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EconomyService.Endpoints;

public static class CurrencyEndpoints
{
    public static void MapCurrencyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/currencies", ListCurrenciesAsync).RequireAuthorization();
    }

    private static async Task<Ok<CurrencyDto[]>> ListCurrenciesAsync(
        ICurrentUser currentUser, EconomyDbContext dbContext, CancellationToken cancellationToken)
    {
        var currencies = await dbContext.Currencies
            .AsNoTracking()
            .Where(c => c.GameId == null || c.GameId == currentUser.GameId)
            .OrderBy(c => c.Code)
            .Select(c => new CurrencyDto(c.Id, c.Code, c.DisplayName, c.Scope, c.GameId))
            .ToArrayAsync(cancellationToken);

        return TypedResults.Ok(currencies);
    }
}
