namespace IdentityService.Options;

// See SeedingOptions -- same reasoning, split into its own flag so exposing OpenAPI/Scalar
// is an explicit choice rather than a side effect of ASPNETCORE_ENVIRONMENT.
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    public bool ExposeOpenApi { get; set; } = true;
}
