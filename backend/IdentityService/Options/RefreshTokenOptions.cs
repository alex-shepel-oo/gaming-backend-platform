using System.ComponentModel.DataAnnotations;

namespace IdentityService.Options;

public sealed class RefreshTokenOptions
{
    public const string SectionName = "RefreshToken";

    [Range(1, int.MaxValue)]
    public int LifetimeDays { get; set; } = 14;

    [Range(1, int.MaxValue)]
    public int FamilyAbsoluteLifetimeDays { get; set; } = 30;
}
