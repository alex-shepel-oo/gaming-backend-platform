using BuildingBlocks.Messaging;

namespace IdentityService.Messaging.Events;

public sealed record UserEmailConfirmedEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }

    public override string Type => "user.email_confirmed";
    public override int Version => 1;
}
