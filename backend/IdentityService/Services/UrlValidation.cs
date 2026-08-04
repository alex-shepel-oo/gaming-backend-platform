namespace IdentityService.Services;

/// <summary>
/// Shared PATCH-field convention: an explicit empty string normalizes to <see langword="null"/>
/// (clear the field), a valid absolute http/https URL is used as-is, anything else is invalid.
/// Callers are expected to skip this entirely when the incoming value is <see langword="null"/>
/// (meaning "leave the field untouched").
/// </summary>
public static class UrlValidation
{
    public static bool TryNormalize(string input, out string? normalized)
    {
        if (input == string.Empty)
        {
            normalized = null;
            return true;
        }

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            normalized = input;
            return true;
        }

        normalized = null;
        return false;
    }
}
