namespace EconomyService.Exceptions;

public sealed class ConversionStatusRaceLostException()
    : Exception("The conversion's status changed before this transition could be applied.");
