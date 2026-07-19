using System.ComponentModel.DataAnnotations;

namespace EconomyService.Options;

public sealed class OutboxDispatcherOptions
{
    public const string SectionName = "OutboxDispatcher";

    [Range(1, int.MaxValue)]
    public int PollIntervalSeconds { get; set; } = 5;

    [Range(1, 1000)]
    public int BatchSize { get; set; } = 20;

    // Ceiling on total publish attempts (initial try plus every Polly retry)
    // before a row is left unsent and excluded from future polls (A.4: park,
    // no DLQ).
    [Range(1, int.MaxValue)]
    public int MaxAttempts { get; set; } = 5;
}
