using BuildingBlocks.Messaging;

namespace IdentityService.Messaging.Events;

public sealed record EmailVerificationRequestedEvent : IntegrationEvent
{
    public required string Email { get; init; }
    public required string Code { get; init; }
    public required string GameName { get; init; }
    public required int ExpiresInMinutes { get; init; }

    public override string Type => "email_verification.requested";
    public override int Version => 1;
}
