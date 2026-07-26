using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace IdentityService.Options;

public sealed class AdminRefreshCookieOptions
{
    public const string SectionName = "AdminCookie";

    [Required]
    public string Name { get; set; } = "gbp_admin_refresh";

    [Required]
    public string Path { get; set; } = "/api/identity/auth";

    public bool HttpOnly => true;

    public bool RequireSecure { get; set; } = true;

    public SameSiteMode SameSite { get; set; } = SameSiteMode.Strict;

    [Range(1, int.MaxValue)]
    public int MaxAgeDays { get; set; } = 14;
}
