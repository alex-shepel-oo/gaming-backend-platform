using EconomyService.Domain.Enums;

namespace EconomyService.Contracts.Responses;

public sealed record ConversionDto(
    Guid ConversionId,
    Guid UserId,
    Guid FromCurrencyId,
    Guid ToCurrencyId,
    decimal FromAmount,
    decimal ToAmount,
    decimal RateApplied,
    ConversionStatus Status,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
