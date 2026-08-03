namespace EmailService.Messaging.Events;

public sealed record DuplicateRegistrationNoticeRequestedPayload
{
    public required string Email { get; init; }
    public required string GameName { get; init; }
}
