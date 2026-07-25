namespace IdentityService.RateLimiting;

public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string Register = "register";
    public const string ConfirmEmail = "confirm-email";
    public const string ResendVerification = "resend-verification";
    public const string RequestPasswordReset = "request-password-reset";
    public const string ResetPassword = "reset-password";
}
