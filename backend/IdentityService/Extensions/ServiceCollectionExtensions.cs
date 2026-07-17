using IdentityService.Persistence;
using IdentityService.Services;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("IdentityDb")));

        return services;
    }

    public static IServiceCollection AddIdentityServices(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();

        return services;
    }
}
