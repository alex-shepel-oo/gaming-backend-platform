using BuildingBlocks.Messaging;

namespace IdentityService.Messaging.Events;

public sealed record DuplicateRegistrationNoticeRequestedEvent : IntegrationEvent
{
    public required string Email { get; init; }
    public required string GameName { get; init; }

    public override string Type => "duplicate_registration_notice.requested";
    public override int Version => 1;
}
