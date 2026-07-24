namespace EconomyService.Exceptions;

public sealed class UnsupportedConversionPairException()
    : Exception("No conversion rate is configured for this currency pair.");
