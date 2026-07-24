namespace EconomyService.Exceptions;

public sealed class IdempotencyKeyConflictException()
    : Exception("The Idempotency-Key has already been used with different conversion parameters.");
