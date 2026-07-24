using BuildingBlocks.Messaging.Outbox;
using EconomyService.Domain;
using EconomyService.Domain.Enums;
using EconomyService.Exceptions;
using EconomyService.Messaging.Events;
using EconomyService.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

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

        try
        {
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
                catch (ConversionStatusRaceLostException)
                {
                    // Not a credit failure - another writer (cancellation)
                    // already moved this conversion past DebitDone before the
                    // credit step's own guarded transition could land.
                    // Whoever won the race owns finishing the conversion.
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Any other failure of the credit step - a constraint
                    // violation, an injected fault, anything - triggers
                    // compensation. This is a business-level reversal, not a
                    // database rollback: the debit transaction already
                    // committed and is not going anywhere.
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
        catch (ConversionStatusRaceLostException)
        {
            // Someone else already advanced this conversion past the status
            // this run expected - stop rather than clobber their outcome.
            return;
        }
    }

    // Compare-and-swap on the conversion's own status column, the same
    // technique LedgerService already uses on Balance.Version: the status
    // moves through a known, monotonic sequence, so "expected old value" is
    // itself a sufficient CAS key - a separate version counter would just
    // duplicate information the status already carries. Internal (rather
    // than private) so the concurrency test can drive it directly to
    // simulate a second writer racing the saga, without the cancellation
    // endpoint that will call it for real existing yet.
    internal async Task<bool> TryTransitionAsync(
        Guid conversionId,
        ConversionStatus from,
        Action<UpdateSettersBuilder<ConversionRequest>> setPropertyCalls,
        CancellationToken cancellationToken)
    {
        var affected = await dbContext.ConversionRequests
            .Where(r => r.Id == conversionId && r.Status == from)
            .ExecuteUpdateAsync(setPropertyCalls, cancellationToken);

        return affected == 1;
    }

    private async Task DebitAsync(ConversionRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        await ledgerService.ConversionDebitAsync(
            new LedgerMutationRequest(
                request.UserId, request.FromCurrencyId, request.FromAmount, $"conversion:{request.Id}:debit", "conversion debit"),
            async (result, ct) =>
            {
                if (!await TryTransitionAsync(
                        request.Id,
                        ConversionStatus.Started,
                        setters => setters
                            .SetProperty(r => r.Status, ConversionStatus.DebitDone)
                            .SetProperty(r => r.UpdatedAt, now),
                        ct))
                {
                    // Losing this CAS means the ledger insert and balance
                    // update above must not stick either - throwing here
                    // rolls back the whole onPosted transaction.
                    throw new ConversionStatusRaceLostException();
                }

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
                if (!await TryTransitionAsync(
                        request.Id,
                        ConversionStatus.DebitDone,
                        setters => setters
                            .SetProperty(r => r.Status, ConversionStatus.Completed)
                            .SetProperty(r => r.UpdatedAt, now),
                        ct))
                {
                    throw new ConversionStatusRaceLostException();
                }

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

    private async Task MarkCompensatingAsync(Guid conversionId, CancellationToken cancellationToken)
    {
        if (!await TryTransitionAsync(
                conversionId,
                ConversionStatus.DebitDone,
                setters => setters
                    .SetProperty(r => r.Status, ConversionStatus.Compensating)
                    .SetProperty(r => r.UpdatedAt, timeProvider.GetUtcNow()),
                cancellationToken))
        {
            throw new ConversionStatusRaceLostException();
        }
    }

    private async Task CompensateAsync(ConversionRequest request, string failureReason, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        await ledgerService.ConversionCompensateAsync(
            new LedgerMutationRequest(
                request.UserId, request.FromCurrencyId, request.FromAmount, $"conversion:{request.Id}:compensation", "conversion compensation"),
            async (result, ct) =>
            {
                if (!await TryTransitionAsync(
                        request.Id,
                        ConversionStatus.Compensating,
                        setters => setters
                            .SetProperty(r => r.Status, ConversionStatus.Failed)
                            .SetProperty(r => r.FailureReason, failureReason)
                            .SetProperty(r => r.UpdatedAt, now),
                        ct))
                {
                    throw new ConversionStatusRaceLostException();
                }

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
