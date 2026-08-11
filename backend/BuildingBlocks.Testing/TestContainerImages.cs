namespace BuildingBlocks.Testing;

/// <summary>
/// Container images every test project's Testcontainers fixtures build against, named once so a
/// version bump is one edit instead of a grep-and-replace across every *.Tests project.
/// </summary>
public static class TestContainerImages
{
    public const string Postgres = "postgres:17-alpine";
    public const string RabbitMq = "rabbitmq:4-management-alpine";
}
