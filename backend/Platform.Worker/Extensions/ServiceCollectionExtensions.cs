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

        var intervalMinutes = configuration.GetSection(CleanupJobOptions.SectionName).GetValue<int?>("IntervalMinutes")
            ?? new CleanupJobOptions().IntervalMinutes;

        services.AddQuartz(quartz =>
        {
            var jobKey = new JobKey(nameof(CleanupExpiredTokensJob));

            quartz.AddJob<CleanupExpiredTokensJob>(job => job.WithIdentity(jobKey));
            quartz.AddTrigger(trigger => trigger
                .ForJob(jobKey)
                .WithIdentity($"{nameof(CleanupExpiredTokensJob)}-trigger")
                .WithSimpleSchedule(schedule => schedule
                    .WithIntervalInMinutes(intervalMinutes)
                    .RepeatForever()));
        });

        return services;
    }
}
