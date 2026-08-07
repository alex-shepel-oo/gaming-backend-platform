using BuildingBlocks.Auth;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Inbox;
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
        // same way AddDbContext defers it: reading it eagerly here would capture whatever
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

        services.AddSingleton<JwksKeySnapshot>();
        services.AddHttpClient<IJwksKeyCache, JwksKeyCache>();
        services.AddHostedService<JwksRefreshHostedService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, IJwksKeyCache>((bearerOptions, jwtOptions, jwksKeyCache) =>
            {
                var options = jwtOptions.Value;

                // Identity issues standard short claim names (sub, game_id, role) with
                // MapInboundClaims off; mapping them here too keeps EconomyClaims/ICurrentUser
                // reading the same names the token actually carries.
                bearerOptions.MapInboundClaims = false;
                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Issuer,
                    ValidAudiences = options.Audiences,
                    IssuerSigningKeyResolver = (_, _, kid, _) => jwksKeyCache.CurrentKeys.Where(key => key.KeyId == kid),
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
                };
            });

        services.AddAuthorization();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        return services;
    }

    public static IServiceCollection AddEconomyServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IBalanceService, BalanceService>();
        services.AddScoped<ILedgerService, LedgerService>();
        services.AddScoped<IConversionCreditFaultInjector, NoOpConversionCreditFaultInjector>();
        services.AddScoped<IConversionSaga, ConversionSaga>();
        services.AddSingleton<ConversionSagaChannel>();
        services.AddScoped<IConversionRequestService, ConversionRequestService>();
        services.AddHostedService<ConversionSagaRunner>();

        services.AddOptions<WelcomeGrantOptions>()
            .Bind(configuration.GetSection(WelcomeGrantOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IWelcomeGrantService, WelcomeGrantService>();

        services.AddOptions<SeedingOptions>()
            .Bind(configuration.GetSection(SeedingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ApiOptions>()
            .Bind(configuration.GetSection(ApiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    public static IServiceCollection AddDeduplicatingEventConsumer(this IServiceCollection services)
    {
        services.AddSingleton<IInboxFaultInjector, NoOpInboxFaultInjector>();
        services.AddHostedService<DeduplicatingEventConsumer>();

        return services;
    }

    public static IServiceCollection AddWelcomeGrantConsumer(this IServiceCollection services)
    {
        services.AddHostedService<UserEmailConfirmedConsumer>();

        return services;
    }
}
