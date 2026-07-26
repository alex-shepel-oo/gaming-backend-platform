using System.ComponentModel.DataAnnotations;

namespace ApiGateway.Options;

public sealed class AdminClientCorsOptions
{
    public const string SectionName = "AdminCors";

    [Required]
    [MinLength(1)]
    public string[] AllowedOrigins { get; set; } = [];
}
