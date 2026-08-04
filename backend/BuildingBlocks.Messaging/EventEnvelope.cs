namespace BuildingBlocks.Messaging;

// The already-serialized shape an outbox row is published as: the dispatcher
// only ever has type/version/payload strings read back from the table, not
// the original strongly-typed IntegrationEvent instance.
public sealed record EventEnvelope(string Type, int Version, string Payload);
