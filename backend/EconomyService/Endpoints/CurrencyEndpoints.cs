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
        EconomyDbContext dbContext, CancellationToken cancellationToken)
    {
        var currencies = await dbContext.Currencies
            .AsNoTracking()
            .OrderBy(c => c.Code)
            .Select(c => new CurrencyDto(c.Id, c.Code, c.DisplayName, c.Scope, c.GameId, c.Decimals, c.IconUrl))
            .ToArrayAsync(cancellationToken);

        return TypedResults.Ok(currencies);
    }
}
