using System.ComponentModel.DataAnnotations;

namespace IdentityService.Options;

// Everything else that used to live here (Provider/From/FromDisplayName/Smtp) moved to EmailService
// along with the actual sending -- IdentityService only needs FrontendBaseUrl now, to build the
// reset link that goes into the PasswordResetRequestedEvent payload, not to send anything itself.
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    [Required]
    [Url]
    public string FrontendBaseUrl { get; set; } = "http://localhost:8080";
}
