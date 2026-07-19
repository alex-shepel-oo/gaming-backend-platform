using EconomyService.Domain;
using EconomyService.Domain.Enums;
using EconomyService.Exceptions;
using EconomyService.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EconomyService.Services;

public sealed class ConversionRequestService(
    EconomyDbContext dbContext,
    IBalanceService balanceService,
    ConversionSagaChannel sagaChannel,
    TimeProvider timeProvider) : IConversionRequestService
{
    public async Task<ConversionCreationResult> CreateOrGetAsync(
        ConversionCreationRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.ConversionRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == request.ConversionId, cancellationToken);

        if (existing is not null)
        {
            return ReplayOrConflict(existing, request);
        }

        var rate = await dbContext.ConversionRates
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.FromCurrencyId == request.FromCurrencyId && r.ToCurrencyId == request.ToCurrencyId, cancellationToken)
            ?? throw new UnsupportedConversionPairException();

        // Checked before the row is ever written - a caller who can't cover
        // the debit never gets a Started conversion sitting in the table.
        var currentBalance = await balanceService.GetBalanceAsync(request.UserId, request.FromCurrencyId, cancellationToken);
        if (currentBalance < request.FromAmount)
        {
            throw new InsufficientFundsException();
        }

        var toCurrency = await dbContext.Currencies
            .AsNoTracking()
            .SingleAsync(c => c.Id == request.ToCurrencyId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var conversionRequest = new ConversionRequest
        {
            Id = request.ConversionId,
            UserId = request.UserId,
            FromCurrencyId = request.FromCurrencyId,
            ToCurrencyId = request.ToCurrencyId,
            GameId = toCurrency.GameId,
            FromAmount = request.FromAmount,
            ToAmount = Math.Round(request.FromAmount * rate.Rate, 2, MidpointRounding.ToEven),
            RateApplied = rate.Rate,
            Status = ConversionStatus.Started,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.ConversionRequests.Add(conversionRequest);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another request raced us with the same conversion id and won;
            // replay its outcome rather than surfacing a duplicate-key error
            // to a caller who did nothing wrong.
            dbContext.ChangeTracker.Clear();
            var racedRequest = await dbContext.ConversionRequests
                .AsNoTracking()
                .SingleAsync(r => r.Id == request.ConversionId, cancellationToken);

            return ReplayOrConflict(racedRequest, request);
        }

        await sagaChannel.Writer.WriteAsync(conversionRequest.Id, cancellationToken);

        return new ConversionCreationResult(conversionRequest, IsReplay: false);
    }

    private static ConversionCreationResult ReplayOrConflict(ConversionRequest existing, ConversionCreationRequest request)
    {
        if (existing.UserId != request.UserId
            || existing.FromCurrencyId != request.FromCurrencyId
            || existing.ToCurrencyId != request.ToCurrencyId
            || existing.FromAmount != request.FromAmount)
        {
            throw new IdempotencyKeyConflictException();
        }

        return new ConversionCreationResult(existing, IsReplay: true);
    }
}
