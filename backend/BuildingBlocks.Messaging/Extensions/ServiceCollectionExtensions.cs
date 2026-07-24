using BuildingBlocks.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Messaging.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the RabbitMQ-backed <see cref="IEventBus"/>, connection and topology
    /// initializer. Must be called before <see cref="AddOutboxDispatcher{TDbContext}"/>,
    /// which resolves <see cref="IEventBus"/> from the container.
    /// </summary>
    public static IServiceCollection AddRabbitMqEventBus(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();
        services.AddSingleton<IEventBus, RabbitMqEventBus>();
        services.AddHostedService<RabbitMqTopologyInitializer>();

        return services;
    }

    public static IServiceCollection AddOutbox<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddScoped<IOutboxWriter, OutboxWriter<TDbContext>>();
        return services;
    }

    /// <summary>
    /// Registers the outbox dispatcher's hosted background service. Requires
    /// <see cref="AddRabbitMqEventBus"/> to have been called first - the
    /// dispatcher resolves <see cref="IEventBus"/> from the container.
    /// </summary>
    public static IServiceCollection AddOutboxDispatcher<TDbContext>(
        this IServiceCollection services, IConfiguration configuration)
        where TDbContext : DbContext
    {
        services.AddOptions<OutboxDispatcherOptions>()
            .Bind(configuration.GetSection(OutboxDispatcherOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHostedService<OutboxDispatcherService<TDbContext>>();

        return services;
    }
}
