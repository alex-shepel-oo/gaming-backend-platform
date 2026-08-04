using EconomyService.Contracts.Responses;
using EconomyService.Exceptions;
using EconomyService.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EconomyService.Endpoints;

internal static class LedgerResultMapping
{
    public static void RequireIdempotencyKey(string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new MissingIdempotencyKeyException();
        }
    }

    // A replayed idempotency key returns the outcome of the original mutation,
    // not a new one - 200 tells the caller nothing was created this time, 201
    // that it was (A.4).
    public static Results<Created<TransactionDto>, Ok<TransactionDto>> ToTransactionResult(LedgerPostResult result)
    {
        var dto = ToDto(result);
        return result.IsReplay ? TypedResults.Ok(dto) : TypedResults.Created((string?)null, dto);
    }

    public static TransactionDto ToDto(LedgerPostResult result) => new(
        result.Entry.Id,
        result.Entry.UserId,
        result.Entry.CurrencyId,
        result.Entry.Amount,
        result.Entry.TransactionType,
        result.Entry.Reason,
        result.Balance,
        result.Entry.CreatedAt);
}
