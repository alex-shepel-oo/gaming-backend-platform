namespace EconomyService.Exceptions;

public sealed class MissingIdempotencyKeyException() : Exception("The Idempotency-Key header is required for this request.");
