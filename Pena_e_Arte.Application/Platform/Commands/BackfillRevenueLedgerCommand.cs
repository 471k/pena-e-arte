using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Platform.Commands;

/// <summary>
/// Seeds the revenue ledger for subscriptions that were already paying before it existed: one
/// New event per currently-Active, contracted-MRR-above-zero subscription that has no ledger row
/// yet, dated at the same conservative billing-window start MrrRules already reconstructs
/// (D3). Idempotent by construction — a subscription with any ledger row, backfilled or
/// otherwise, is left untouched, so a rerun is a no-op.
/// </summary>
public record BackfillRevenueLedgerCommand : IRequest<BackfillRevenueLedgerResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.RevenueLedgerBackfilled;
    public string AuditTargetType => AuditTargetTypes.Platform;
    public Guid AuditTargetId => Guid.Empty; // platform-wide, no single target

    // Set by the handler before it returns; read back by AuditLogBehavior for the metadata.
    public int? Created { get; set; }
    public int? SkippedAlreadyInLedger { get; set; }
    public int? SkippedNotBilling { get; set; }
}

public class BackfillRevenueLedgerHandler(
    IAppDbContext db,
    ILogger<BackfillRevenueLedgerHandler> logger)
    : IRequestHandler<BackfillRevenueLedgerCommand, BackfillRevenueLedgerResponse>
{
    public const string Source = "Backfill";

    public async Task<BackfillRevenueLedgerResponse> Handle(BackfillRevenueLedgerCommand command, CancellationToken ct)
    {
        List<SubscriptionRevenueInput> inputs = await MrrInputLoader.LoadAsync(db, ct);

        HashSet<Guid> alreadyInLedger = (await db.SubscriptionRevenueEvents
                .Select(e => e.SubscriptionId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        DateTime now = DateTime.UtcNow;
        int created = 0;
        int skippedAlreadyInLedger = 0;
        int skippedNotBilling = 0;

        foreach (SubscriptionRevenueInput input in inputs)
        {
            Subscription subscription = input.Subscription;
            decimal mrr = MrrRules.MonthlyEquivalent(subscription);

            if (subscription.Status != SubscriptionStatus.Active || mrr <= 0m)
            {
                skippedNotBilling++;
                continue;
            }

            if (alreadyInLedger.Contains(subscription.Id))
            {
                skippedAlreadyInLedger++;
                continue;
            }

            (DateTime Start, DateTime? End)? window = MrrRules.BillingWindow(input, now);
            if (window is null)
            {
                skippedNotBilling++;
                continue;
            }

            DateTime start = window.Value.Start;
            RevenueEventRecorder.Record(db, subscription, 0m, mrr, RevenueEventType.New, Source, stripeEventId: null, start);

            // A suspended studio's Active subscription is not in headline MRR (D6): pair the New
            // with a Paused so the ledger agrees with MrrRules now, and a later unsuspend has
            // something to resume.
            if (!input.StudioIsActive)
            {
                // start + 1 tick when there's no later suspension timestamp: a tie on OccurredAt
                // would fall back to CreatedAt, which can be identical at the clock's resolution.
                DateTime pausedAt = input.SuspendedAt is DateTime suspendedAt && suspendedAt > start
                    ? suspendedAt
                    : start.AddTicks(1);
                RevenueEventRecorder.Record(db, subscription, mrr, mrr, RevenueEventType.Paused, Source, stripeEventId: null, pausedAt);
            }

            created++;
        }

        await db.SaveChangesAsync(ct);

        command.Created = created;
        command.SkippedAlreadyInLedger = skippedAlreadyInLedger;
        command.SkippedNotBilling = skippedNotBilling;

        logger.LogInformation(
            "Revenue-ledger backfill: {Created} subscription(s) seeded, "
            + "{SkippedAlreadyInLedger} skipped (already in ledger), {SkippedNotBilling} skipped (not currently billing)",
            created, skippedAlreadyInLedger, skippedNotBilling);

        return new BackfillRevenueLedgerResponse(created, skippedAlreadyInLedger, skippedNotBilling);
    }
}

// Required by the "no endpoint without a validator" rule, even with no properties.
public class BackfillRevenueLedgerValidator : AbstractValidator<BackfillRevenueLedgerCommand>
{
    // No properties to validate — validator satisfies the registration convention.
}
