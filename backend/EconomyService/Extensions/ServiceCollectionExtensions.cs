using EconomyService.Infrastructure;
using EconomyService.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EconomyService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEconomyPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<EconomyDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("EconomyDb")));

        return services;
    }

    public static IServiceCollection AddEconomyExceptionHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }
}
