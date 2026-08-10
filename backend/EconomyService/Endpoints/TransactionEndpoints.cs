using System.ComponentModel.DataAnnotations;
using EconomyService.Auth;
using EconomyService.Contracts.Requests;
using EconomyService.Contracts.Responses;
using EconomyService.Domain.Enums;
using EconomyService.Persistence;
using EconomyService.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EconomyService.Endpoints;

public static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/transactions");

        group.MapPost("/grant", GrantAsync).RequireAuthorization(policy => policy.RequireClaim(
            EconomyClaims.Perms, Permissions.GameBalanceAdjust, Permissions.PlatformBalanceAdjust));
        group.MapPost("/spend", SpendAsync).RequireAuthorization();
        group.MapGet("/me", GetMyHistoryAsync).RequireAuthorization();
    }

    private static async Task<Results<Created<TransactionDto>, Ok<TransactionDto>, ForbidHttpResult>> GrantAsync(
        GrantRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ICurrentUser currentUser,
        EconomyDbContext dbContext,
        ILedgerService ledgerService,
        CancellationToken cancellationToken)
    {
        LedgerResultMapping.RequireIdempotencyKey(idempotencyKey);

        var targetGameId = await dbContext.Currencies
            .Where(c => c.Id == request.CurrencyId)
            .Select(c => c.GameId)
            .SingleAsync(cancellationToken);

        if (!BalanceScopeGuard.CanAdjust(currentUser, targetGameId))
        {
            return TypedResults.Forbid();
        }

        var result = await ledgerService.GrantAsync(
            new LedgerMutationRequest(request.UserId, request.CurrencyId, request.Amount, idempotencyKey!, request.Reason),
            cancellationToken);

        var dto = LedgerResultMapping.ToDto(result);
        return result.IsReplay ? TypedResults.Ok(dto) : TypedResults.Created((string?)null, dto);
    }

    private static async Task<Results<Created<TransactionDto>, Ok<TransactionDto>>> SpendAsync(
        SpendRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ICurrentUser currentUser,
        ILedgerService ledgerService,
        CancellationToken cancellationToken)
    {
        LedgerResultMapping.RequireIdempotencyKey(idempotencyKey);

        var result = await ledgerService.SpendAsync(
            new LedgerMutationRequest(currentUser.UserId, request.CurrencyId, request.Amount, idempotencyKey!, request.Reason),
            cancellationToken);

        return LedgerResultMapping.ToTransactionResult(result);
    }

    private static async Task<Ok<PagedResult<TransactionHistoryEntryDto>>> GetMyHistoryAsync(
        Guid? currencyId,
        TransactionType[]? types,
        ICurrentUser currentUser,
        EconomyDbContext dbContext,
        CancellationToken cancellationToken,
        [Range(1, int.MaxValue)] int page = 1,
        [Range(1, 100)] int pageSize = 20)
    {
        var query = dbContext.LedgerEntries.Where(e => e.UserId == currentUser.UserId);

        if (currencyId is not null)
        {
            query = query.Where(e => e.CurrencyId == currencyId);
        }

        // Repeatable ?types=Grant&types=Spend rather than a single value: the
        // "Conversions" filter chip needs both ConversionOut and ConversionIn at once.
        if (types is { Length: > 0 })
        {
            query = query.Where(e => types.Contains(e.TransactionType));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new TransactionHistoryEntryDto(
                e.Id, e.CurrencyId, e.Amount, e.TransactionType, e.Reason, e.CreatedAt, e.IdempotencyKey))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PagedResult<TransactionHistoryEntryDto>(items, page, pageSize, totalCount));
    }
}
