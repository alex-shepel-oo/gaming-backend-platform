namespace BuildingBlocks.Telemetry;

/// <summary>
/// OpenTelemetry semantic-convention attribute names shared across services, so the same
/// identifier is spelled identically everywhere it's used to tag an Activity or push a log
/// property, rather than each call site re-typing the string.
/// </summary>
public static class OtelConventions
{
    public const string EnduserId = "enduser.id";
}
