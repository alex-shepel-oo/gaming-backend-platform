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

    private static List<FileRoute> LoadMergedRoutes(string environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(ConfigDirectory, "ocelot.json"), optional: false)
            .AddJsonFile(Path.Combine(ConfigDirectory, $"ocelot.{environment}.json"), optional: true)
            .Build();

        return configuration.Get<FileConfiguration>()!.Routes;
    }
}
