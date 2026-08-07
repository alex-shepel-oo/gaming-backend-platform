using Microsoft.EntityFrameworkCore;
using Platform.Worker.Jobs;
using Platform.Worker.Options;
using Platform.Worker.Persistence;
using Quartz;

namespace Platform.Worker.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCleanupJob(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CleanupJobOptions>()
            .Bind(configuration.GetSection(CleanupJobOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContextFactory<IdentityCleanupDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("IdentityDb")));
        services.AddDbContextFactory<EconomyCleanupDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("EconomyDb")));

        // AddQuartz's callback runs before the container is built, so IOptions isn't available yet.
        var cleanupJobOptions = configuration.GetSection(CleanupJobOptions.SectionName).Get<CleanupJobOptions>()
            ?? new CleanupJobOptions();

        services.AddQuartz(quartz =>
        {
            var jobKey = new JobKey(nameof(CleanupExpiredTokensJob));

            quartz.AddJob<CleanupExpiredTokensJob>(job => job.WithIdentity(jobKey));
            quartz.AddTrigger(trigger => trigger
                .ForJob(jobKey)
                .WithIdentity($"{nameof(CleanupExpiredTokensJob)}-trigger")
                .WithSimpleSchedule(schedule => schedule
                    .WithIntervalInMinutes(cleanupJobOptions.IntervalMinutes)
                    .RepeatForever()));
        });

        return services;
    }
}
