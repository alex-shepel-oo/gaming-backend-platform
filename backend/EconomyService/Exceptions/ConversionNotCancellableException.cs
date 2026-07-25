namespace EconomyService.Exceptions;

public sealed class ConversionNotCancellableException()
    : Exception("This conversion has already reached a terminal or compensating status and cannot be cancelled.");
