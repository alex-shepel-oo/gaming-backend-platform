using BuildingBlocks.Messaging;
using EmailService.Messaging;
using EmailService.Options;
using EmailService.Services.Email;
using EmailService.Services.Email.Templates;
using Microsoft.Extensions.DependencyInjection;

namespace EmailService.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddEmailSending(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IEmailTemplateRenderer, EmailTemplateRenderer>();

        var provider = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>()?.Provider;

        if (string.Equals(provider, "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender, NoopEmailSender>();
        }

        return services;
    }

    // Registers only the connection primitive, not BuildingBlocks.Messaging's AddRabbitMqEventBus:
    // that method also wires IEventBus and RabbitMqTopologyInitializer, both producer-side concerns
    // (declaring the exchange, publishing). EmailService never publishes -- it only consumes off
    // identity-service's own gbp.identity exchange, which identity-service's topology initializer
    // already declares.
    public static IServiceCollection AddEmailMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();
        services.AddHostedService<EmailVerificationRequestedConsumer>();
        services.AddHostedService<PasswordResetRequestedConsumer>();
        services.AddHostedService<DuplicateRegistrationNoticeRequestedConsumer>();

        return services;
    }
}
