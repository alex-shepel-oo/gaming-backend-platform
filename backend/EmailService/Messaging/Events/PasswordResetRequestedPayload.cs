namespace EmailService.Messaging.Events;

public sealed record PasswordResetRequestedPayload
{
    public required string Email { get; init; }
    public required string ResetLink { get; init; }
    public required int ExpiresInMinutes { get; init; }
}
