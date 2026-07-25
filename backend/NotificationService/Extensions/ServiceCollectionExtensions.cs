using System.Text;
using BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NotificationService.Infrastructure;
using NotificationService.Options;

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
    // exchange, publishing). NotificationService never publishes -- it only
    // reads from RabbitMQ, currently just to probe the connection for
    // /health/ready. The consumer added in a later session binds its own
    // queue directly against this same connection instead.
    public static IServiceCollection AddNotificationMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();

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

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearerOptions, jwtOptions) =>
            {
                var options = jwtOptions.Value;

                // Same MapInboundClaims = false convention as every other
                // service: the token carries short names (sub, game_id) as-is.
                bearerOptions.MapInboundClaims = false;
                bearerOptions.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = options.Issuer,
                    ValidAudiences = options.Audiences,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key)),
                    ClockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds),
                };
            });

        services.AddAuthorization();

        return services;
    }
}
