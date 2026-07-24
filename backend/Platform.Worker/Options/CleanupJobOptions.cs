using System.ComponentModel.DataAnnotations;

namespace Platform.Worker.Options;

public sealed class CleanupJobOptions
{
    public const string SectionName = "CleanupJob";

    [Range(1, int.MaxValue)]
    public int IntervalMinutes { get; set; } = 15;

    // Outbox rows are only ever deleted once processed_at is set (never the
    // undelivered ones) and past this many days, giving a wide observation
    // window before a dispatched event disappears.
    [Range(1, int.MaxValue)]
    public int OutboxRetentionDays { get; set; } = 7;
}
