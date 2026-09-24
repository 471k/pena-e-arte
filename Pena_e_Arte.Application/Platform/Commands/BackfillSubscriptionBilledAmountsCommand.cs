using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Platform.Commands;

/// <summary>
/// R6 — snapshots BilledUnitAmount on every pre-Batch-2b subscription so MrrRules stops
/// falling back to the live PlanPrice list for them. Idempotent: only rows still null are
/// touched, so it is safe to re-run (e.g. after a partial Stripe outage). Card-billed
/// subscriptions are snapshotted from Stripe; cash-billed ones from their plan's Monthly
/// price, and listed back for the admin to review since nothing is billed by this action.
/// </summary>
public record BackfillSubscriptionBilledAmountsCommand : IRequest<BackfillSubscriptionBilledAmountsResponse>;

public class BackfillSubscriptionBilledAmountsHandler(
    IAppDbContext db,
    IStripeBillingService stripe,
    ILogger<BackfillSubscriptionBilledAmountsHandler> logger)
    : IRequestHandler<BackfillSubscriptionBilledAmountsCommand, BackfillSubscriptionBilledAmountsResponse>
{
    public async Task<BackfillSubscriptionBilledAmountsResponse> Handle(
        BackfillSubscriptionBilledAmountsCommand command, CancellationToken ct)
    {
        List<Subscription> cardBilled = await db.Subscriptions
            .Where(s => s.StripeSubscriptionId != null && s.BilledUnitAmount == null)
            .ToListAsync(ct);

        int cardBilledUpdated = 0;
        int cardBilledSkipped = 0;

        foreach (Subscription sub in cardBilled)
        {
            StripePriceInfo? price = await stripe.GetSubscriptionBilledPriceAsync(sub.StripeSubscriptionId!, ct);
            if (price is null)
            {
                cardBilledSkipped++;
                continue;
            }

            sub.BilledUnitAmount = (price.UnitAmount ?? 0) / 100m;
            sub.BilledQuantity = 1;
            sub.BilledCurrency = price.Currency;
            cardBilledUpdated++;
        }

        List<Subscription> cashBilled = await db.Subscriptions
            .Include(s => s.Plan)
                .ThenInclude(p => p!.Prices)
            .Where(s => s.StripeSubscriptionId == null && s.BilledUnitAmount == null)
            .ToListAsync(ct);

        List<CashBilledSnapshotResponse> cashBilledSnapshots = [];
        foreach (Subscription sub in cashBilled)
        {
            decimal monthlyPrice = sub.Plan?.Prices
                .FirstOrDefault(pp => pp.Interval == Domain.Enums.BillingInterval.Monthly)?.Price ?? 0m;

            sub.BilledUnitAmount = monthlyPrice;
            sub.BilledQuantity = 1;
            sub.BilledCurrency = MrrRules.PlatformCurrency;
            cashBilledSnapshots.Add(new CashBilledSnapshotResponse(sub.StudioId, monthlyPrice));
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Billed-amount backfill: {CardBilledUpdated} card-billed snapshotted, "
            + "{CardBilledSkipped} card-billed skipped (not found in Stripe), "
            + "{CashBilledCount} cash-billed snapshotted",
            cardBilledUpdated, cardBilledSkipped, cashBilledSnapshots.Count);

        return new BackfillSubscriptionBilledAmountsResponse(cardBilledUpdated, cardBilledSkipped, cashBilledSnapshots);
    }
}

// Required by the "no endpoint without a validator" rule, even with no properties.
public class BackfillSubscriptionBilledAmountsValidator : AbstractValidator<BackfillSubscriptionBilledAmountsCommand>
{
    // No properties to validate — validator satisfies the registration convention.
}
