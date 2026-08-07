namespace EconomyService.Options;

// Deliberately decoupled from ASPNETCORE_ENVIRONMENT: seeding used to run whenever
// IsDevelopment() was true, which also happens to be what every k8s ConfigMap sets today.
// Left unset, this defaults to the same on/off behavior IsDevelopment() gave before, so no
// existing docker compose/kind deployment changes; a real deployment can now turn
// seeding off without also losing Scalar, or the other way around.
public sealed class SeedingOptions
{
    public const string SectionName = "Seeding";

    public bool Enabled { get; set; } = true;
}
