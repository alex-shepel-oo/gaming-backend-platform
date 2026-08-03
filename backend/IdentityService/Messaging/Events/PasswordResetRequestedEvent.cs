using BuildingBlocks.Messaging;

namespace IdentityService.Messaging.Events;

public sealed record PasswordResetRequestedEvent : IntegrationEvent
{
    public required string Email { get; init; }
    public required string ResetLink { get; init; }
    public required int ExpiresInMinutes { get; init; }

    public override string Type => "password_reset.requested";
    public override int Version => 1;
}
