using System.Text;
using EconomyService.Auth;
using EconomyService.Infrastructure;
using EconomyService.Messaging;
using EconomyService.Options;
using EconomyService.Persistence;
using EconomyService.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EconomyService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEconomyPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<EconomyDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("EconomyDb")));

        return services;
    }

    public static IServiceCollection AddEconomyHealthChecks(this IServiceCollection services)
    {
        // Connection string is read lazily from IConfiguration at check-execution time, the
        // same way AddDbContext defers it -- reading it eagerly here would capture whatever
        // IConfiguration held at service-registration time, missing overrides (e.g. in tests)
        // that land afterwards.
        services.AddHealthChecks()
            .AddNpgSql(
                sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("EconomyDb")!,
                name: "postgresql",
                tags: ["ready"])
            .AddRabbitMQ(
                sp => sp.GetRequiredService<IRabbitMqConnection>().GetConnectionAsync(),
                name: "rabbitmq",
                tags: ["ready"]);

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

    public static IServiceCollection AddEconomyAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptions) =>
            {
                var options = jwtOptions.Value;

                // Identity issues standard short claim names (sub, game_id, role) with
                // MapInboundClaims off; mapping them here too keeps EconomyClaims/ICurrentUser
                // reading the same names the token actually carries.
                bearerOptions.MapInboundClaims = false;
                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key)),
                    ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
                };
            });

        services.AddAuthorization(AuthorizationPolicies.Configure);

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        return services;
    }

    public static IServiceCollection AddEconomyServices(this IServiceCollection services)
    {
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IBalanceService, BalanceService>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<ILedgerService, LedgerService>();

        return services;
    }

    public static IServiceCollection AddEconomyMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();
        services.AddHostedService<RabbitMqTopologyInitializer>();

        return services;
    }

    public static IServiceCollection AddOutboxDispatcher(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OutboxDispatcherOptions>()
            .Bind(configuration.GetSection(OutboxDispatcherOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IEventBus, RabbitMqEventBus>();
        services.AddHostedService<OutboxDispatcherService>();

        return services;
    }
}
