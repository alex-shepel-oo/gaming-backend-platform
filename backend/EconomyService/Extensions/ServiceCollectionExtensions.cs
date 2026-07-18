using EconomyService.Persistence;
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
}
