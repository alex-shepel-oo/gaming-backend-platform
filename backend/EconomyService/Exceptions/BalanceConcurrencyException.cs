namespace EconomyService.Exceptions;

public sealed class BalanceConcurrencyException() : Exception("The balance could not be updated after repeated concurrent writes.");
