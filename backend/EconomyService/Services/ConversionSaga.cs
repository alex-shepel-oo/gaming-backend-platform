using BuildingBlocks.Messaging.Outbox;
using EconomyService.Domain;
using EconomyService.Domain.Enums;
using EconomyService.Messaging.Events;
using EconomyService.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EconomyService.Services;

// Runs the currency conversion saga in-process and sequentially: a debit
// transaction against the platform currency, then a credit transaction
// against the game currency, with a compensating transaction reversing the
// debit if credit fails. This is deliberately not choreography over the
// message bus - both currencies belong to this service and there is no
// second participant reacting to an event (ADR-0010 addendum). Each
// transition commits on its own, so a crash mid-saga leaves conversion_id's
// status as a durable record of exactly which step was reached, rather than
// an ambiguous one. The outbox events below are for observers - the
// deduplicating consumer added later in this group is one - they never
// drive the saga's own steps.
public sealed class ConversionSaga(
    EconomyDbContext dbContext,
    ILedgerService ledgerService,
    IOutboxWriter outboxWriter,
    IConversionCreditFaultInjector creditFaultInjector,
    TimeProvider timeProvider) : IConversionSaga
{
    public async Task ExecuteAsync(Guid conversionId, CancellationToken cancellationToken = default)
    {
        var request = await dbContext.ConversionRequests
            .AsNoTracking()
            .SingleAsync(r => r.Id == conversionId, cancellationToken);

        var status = request.Status;

        // A terminal status means a previous run already finished this
        // conversion - re-entering here (a redelivered channel item, a
        // repeated call) must not post a second pair of ledger entries.
        if (status is ConversionStatus.Completed or ConversionStatus.Failed)
        {
            return;
        }

        if (status == ConversionStatus.Started)
        {
            await DebitAsync(request, cancellationToken);
            status = ConversionStatus.DebitDone;
        }

        if (status == ConversionStatus.DebitDone)
        {
            string failureReason;

            try
            {
                await creditFaultInjector.BeforeCreditAsync(request, cancellationToken);
                await CreditAsync(request, cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Any failure of the credit step - a constraint violation, an
                // injected fault, anything - triggers compensation. This is a
                // business-level reversal, not a database rollback: the debit
                // transaction already committed and is not going anywhere.
                failureReason = ex.Message;
            }

            await MarkCompensatingAsync(request.Id, cancellationToken);
            await CompensateAsync(request, failureReason, cancellationToken);
            return;
        }

        if (status == ConversionStatus.Compensating)
        {
            await CompensateAsync(request, request.FailureReason ?? "credit step failed", cancellationToken);
        }
    }

    private async Task DebitAsync(ConversionRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        await ledgerService.ConversionDebitAsync(
            new LedgerMutationRequest(
                request.UserId, request.FromCurrencyId, request.FromAmount, $"conversion:{request.Id}:debit", "conversion debit"),
            async (result, ct) =>
            {
                await dbContext.ConversionRequests
                    .Where(r => r.Id == request.Id)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(r => r.Status, ConversionStatus.DebitDone)
                            .SetProperty(r => r.UpdatedAt, now),
                        ct);

                await outboxWriter.WriteAsync(
                    new ConversionDebitedEvent
                    {
                        Id = Guid.CreateVersion7(),
                        OccurredAt = now,
                        ConversionRequestId = request.Id,
                        UserId = request.UserId,
                        FromCurrencyId = request.FromCurrencyId,
                        FromAmount = request.FromAmount,
                        LedgerEntryId = result.Entry.Id,
                    },
                    ct);
            },
            cancellationToken);
    }

    private async Task CreditAsync(ConversionRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        await ledgerService.ConversionCreditAsync(
            new LedgerMutationRequest(
                request.UserId, request.ToCurrencyId, request.ToAmount, $"conversion:{request.Id}:credit", "conversion credit"),
            async (result, ct) =>
            {
                await dbContext.ConversionRequests
                    .Where(r => r.Id == request.Id)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(r => r.Status, ConversionStatus.Completed)
                            .SetProperty(r => r.UpdatedAt, now),
                        ct);

                await outboxWriter.WriteAsync(
                    new ConversionCompletedEvent
                    {
                        Id = Guid.CreateVersion7(),
                        OccurredAt = now,
                        ConversionRequestId = request.Id,
                        UserId = request.UserId,
                        ToCurrencyId = request.ToCurrencyId,
                        ToAmount = request.ToAmount,
                        LedgerEntryId = result.Entry.Id,
                    },
                    ct);
            },
            cancellationToken);
    }

    private Task<int> MarkCompensatingAsync(Guid conversionId, CancellationToken cancellationToken) =>
        dbContext.ConversionRequests
            .Where(r => r.Id == conversionId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(r => r.Status, ConversionStatus.Compensating)
                    .SetProperty(r => r.UpdatedAt, timeProvider.GetUtcNow()),
                cancellationToken);

    private async Task CompensateAsync(ConversionRequest request, string failureReason, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        await ledgerService.ConversionCompensateAsync(
            new LedgerMutationRequest(
                request.UserId, request.FromCurrencyId, request.FromAmount, $"conversion:{request.Id}:compensation", "conversion compensation"),
            async (result, ct) =>
            {
                await dbContext.ConversionRequests
                    .Where(r => r.Id == request.Id)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(r => r.Status, ConversionStatus.Failed)
                            .SetProperty(r => r.FailureReason, failureReason)
                            .SetProperty(r => r.UpdatedAt, now),
                        ct);

                await outboxWriter.WriteAsync(
                    new ConversionFailedEvent
                    {
                        Id = Guid.CreateVersion7(),
                        OccurredAt = now,
                        ConversionRequestId = request.Id,
                        UserId = request.UserId,
                        FromCurrencyId = request.FromCurrencyId,
                        FromAmount = request.FromAmount,
                        Reason = failureReason,
                        CompensatingLedgerEntryId = result.Entry.Id,
                    },
                    ct);
            },
            cancellationToken);
    }
}
