using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Options;

public sealed class PlayerClientCorsOptions
{
    public const string SectionName = "Cors";

    [Required]
    [MinLength(1)]
    public string[] AllowedOrigins { get; set; } = [];
}
