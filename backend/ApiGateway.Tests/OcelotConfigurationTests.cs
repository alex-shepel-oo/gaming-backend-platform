using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Ocelot.Configuration.File;
using Xunit;

namespace ApiGateway.Tests;

public class OcelotConfigurationTests
{
    private static readonly string ConfigDirectory = Path.Combine(AppContext.BaseDirectory, "OcelotConfig");

    private static readonly string[] AnonymousUpstreamPathTemplates =
    [
        "/api/identity/auth/{everything}",
        "/openapi/identity/v1.json",
    ];

    public static TheoryData<string> OcelotFiles =>
    [
        "ocelot.json",
        "ocelot.Development.json",
        "ocelot.Kubernetes.json",
    ];

    public static TheoryData<string> Environments => ["Development", "Kubernetes"];

    [Theory]
    [MemberData(nameof(OcelotFiles))]
    public void File_ParsesIntoOcelotFileConfiguration(string fileName)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(ConfigDirectory, fileName), optional: false)
            .Build();

        var fileConfiguration = configuration.Get<FileConfiguration>();

        fileConfiguration.Should().NotBeNull();
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public void MergedRoutes_HaveEitherServiceNameOrDownstreamHostAndPorts(string environment)
    {
        var routes = LoadMergedRoutes(environment);

        foreach (var route in routes)
        {
            var hasServiceName = !string.IsNullOrWhiteSpace(route.ServiceName);
            var hasHostAndPorts = route.DownstreamHostAndPorts.Count > 0;

            (hasServiceName ^ hasHostAndPorts).Should().BeTrue(
                $"route '{route.UpstreamPathTemplate}' must resolve through exactly one of ServiceName or DownstreamHostAndPorts");
        }
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public void MergedRoutes_UpstreamPathTemplatesDoNotOverlap(string environment)
    {
        var routes = LoadMergedRoutes(environment);

        routes.Select(r => r.UpstreamPathTemplate).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [MemberData(nameof(Environments))]
    public void ProtectedRoutes_HaveAuthenticationProviderKeys(string environment)
    {
        var routes = LoadMergedRoutes(environment);

        foreach (var route in routes)
        {
            var isAnonymous = AnonymousUpstreamPathTemplates.Contains(route.UpstreamPathTemplate);
            var hasAuthenticationProviderKeys = route.AuthenticationOptions?.AuthenticationProviderKeys is { Length: > 0 };

            hasAuthenticationProviderKeys.Should().Be(!isAnonymous,
                $"route '{route.UpstreamPathTemplate}' should {(isAnonymous ? "stay anonymous" : "require a bearer token")}");
        }
    }

    // ocelot.json and ocelot.{Environment}.json merge as chained AddJsonFile
    // configuration sources, which IConfiguration flattens to keys like
    // "Routes:9:ServiceName": arrays merge by index, not by matching
    // UpstreamPathTemplate. A route appended to ocelot.json without a
    // matching entry appended to the environment files doesn't fail the
    // build or throw at startup; it just silently loses its host
    // resolution, because it either lands past the end of the shorter
    // array or shifts onto an unrelated route's entry.
    [Fact]
    public void AllEnvironmentFilesHaveMatchingRouteCount()
    {
        var baseCount = LoadFileRoutes("ocelot.json").Count;

        foreach (var fileName in new[] { "ocelot.Development.json", "ocelot.Kubernetes.json" })
        {
            LoadFileRoutes(fileName).Count.Should().Be(baseCount,
                $"{fileName} must carry one host-resolution entry per route in ocelot.json, in the same order");
        }
    }

    private static List<FileRoute> LoadFileRoutes(string fileName)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(ConfigDirectory, fileName), optional: false)
            .Build();

        return configuration.Get<FileConfiguration>()!.Routes;
    }

    private static List<FileRoute> LoadMergedRoutes(string environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(ConfigDirectory, "ocelot.json"), optional: false)
            .AddJsonFile(Path.Combine(ConfigDirectory, $"ocelot.{environment}.json"), optional: true)
            .Build();

        return configuration.Get<FileConfiguration>()!.Routes;
    }
}
