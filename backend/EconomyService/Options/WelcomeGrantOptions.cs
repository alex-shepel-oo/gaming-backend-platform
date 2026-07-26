using System.ComponentModel.DataAnnotations;

namespace EconomyService.Options;

public sealed class WelcomeGrantOptions
{
    public const string SectionName = "WelcomeGrant";

    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; } = 100m;

    [Required]
    public string CurrencyCode { get; set; } = "PLATFORM_CREDITS";
}
