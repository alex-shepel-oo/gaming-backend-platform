namespace EconomyService.Exceptions;

public sealed class InsufficientFundsException() : Exception("The balance does not have enough funds to cover this transaction.");
