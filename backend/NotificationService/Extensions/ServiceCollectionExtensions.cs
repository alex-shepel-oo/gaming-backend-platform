using BuildingBlocks.Auth;
using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NotificationService.Auth;
using NotificationService.Infrastructure;
using NotificationService.Messaging;

namespace NotificationService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNotificationExceptionHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        });

        services.AddExceptionHandler<GlobalExceptionHandler>();

        return services;
    }

    // Registers only the connection primitive, not BuildingBlocks.Messaging's
    // AddRabbitMqEventBus: that method also wires IEventBus and
    // RabbitMqTopologyInitializer, both producer-side concerns (declaring the
    // exchange, publishing). NotificationService never publishes; it only
    // reads from RabbitMQ, both to probe the connection for /health/ready and,
    // via BalanceChangedConsumer, to bind its own queue directly against this
    // same connection.
    public static IServiceCollection AddNotificationMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();
        services.AddHostedService<BalanceChangedConsumer>();

        return services;
    }

    public static IServiceCollection AddNotificationHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddRabbitMQ(
                sp => sp.GetRequiredService<IRabbitMqConnection>().GetConnectionAsync(),
                name: "rabbitmq",
                tags: ["ready"]);

        return services;
    }

    public static IServiceCollection AddNotificationAuthentication(this IServiceCollection services, IConfiguration configuration)
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

                // Same MapInboundClaims = false convention as every other
                // service: the token carries short names (sub, game_id) as-is.
                bearerOptions.MapInboundClaims = false;
                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Issuer,
                    ValidAudiences = options.Audiences,
                    IssuerSigningKeyResolver = (_, _, kid, _) => jwksKeyCache.CurrentKeys.Where(key => key.KeyId == kid),
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
                };

                // Browsers cannot attach an Authorization header to the WebSocket
                // handshake, so the SignalR client sends the token as a query
                // string instead. Scoped to the hub path only: accepting a
                // query-string token anywhere else would widen where a token can
                // leak (proxy logs, browser history) beyond the one place that
                // needs it.
                bearerOptions.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(accessToken) &&
                            context.HttpContext.Request.Path.StartsWithSegments("/hubs/notifications"))
                        {
                            context.Token = accessToken;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization();

        return services;
    }

    public static IServiceCollection AddNotificationSignalR(this IServiceCollection services)
    {
        services.AddSignalR();
        services.AddSingleton<IUserIdProvider, SubClaimUserIdProvider>();

        return services;
    }
}
