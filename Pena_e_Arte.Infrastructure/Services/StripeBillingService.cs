using Pena_e_Arte.Domain.Interfaces;
using Stripe;
using Stripe.Checkout;

namespace Pena_e_Arte.Infrastructure.Services;

public class StripeBillingService(
    CustomerService customerService,
    SubscriptionService subscriptionService,
    SubscriptionScheduleService scheduleService,
    SessionService checkoutSessions)
    : IStripeBillingService
{
    public async Task<string> CreateCustomerAsync(string email, CancellationToken ct)
    {
        CustomerCreateOptions options = new() { Email = email };
        Customer customer = await customerService.CreateAsync(options, null, ct);
        return customer.Id;
    }

    public async Task<string> CreateSubscriptionCheckoutAsync(
        string customerId, string priceId, string clientReferenceId,
        string successUrl, string cancelUrl, string? couponId, DateTime? trialEnd, CancellationToken ct)
    {
        SessionCreateOptions options = new()
        {
            Mode              = "subscription",
            Customer          = customerId,
            ClientReferenceId = clientReferenceId,
            LineItems         = new List<SessionLineItemOptions> { new() { Price = priceId, Quantity = 1 } },
            Discounts         = couponId is not null
                ? new List<SessionDiscountOptions> { new() { Coupon = couponId } }
                : null,
            // Defer the first charge to the already-paid-through date (cash credit).
            SubscriptionData  = trialEnd is DateTime end
                ? new SessionSubscriptionDataOptions { TrialEnd = end }
                : null,
            SuccessUrl        = successUrl,
            CancelUrl         = cancelUrl,
            // No payment_method_types — Stripe selects eligible methods dynamically.
        };

        Session session = await checkoutSessions.CreateAsync(options, null, ct);
        return session.Url;
    }

    public async Task<CheckoutSubscriptionResult?> GetCheckoutSubscriptionAsync(string sessionId, CancellationToken ct)
    {
        SessionGetOptions options = new() { Expand = new List<string> { "subscription" } };

        Session session;
        try
        {
            session = await checkoutSessions.GetAsync(sessionId, options, null, ct);
        }
        catch (StripeException)
        {
            return null;
        }

        Stripe.Subscription? sub = session.Subscription;
        string?  priceId   = sub?.Items?.Data?.FirstOrDefault()?.Price?.Id;
        DateTime periodEnd = sub?.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd ?? DateTime.UtcNow.AddMonths(1);

        // "paid" covers zero-amount invoices (100% coupon); "no_payment_required" covers setup-mode sessions.
        bool complete = session.Status == "complete"
                     && session.PaymentStatus is "paid" or "no_payment_required";

        return new CheckoutSubscriptionResult(
            complete,
            sub?.Id ?? session.SubscriptionId,
            session.CustomerId,
            session.ClientReferenceId,
            priceId,
            periodEnd,
            HasDiscount: session.Discounts?.Count > 0);
    }

    public async Task<(string SubscriptionId, DateTime CurrentPeriodEnd)> CreateSubscriptionAsync(
        string customerId, string priceId, string? couponId, CancellationToken ct)
    {
        SubscriptionCreateOptions options = new()
        {
            Customer  = customerId,
            Items     = new List<SubscriptionItemOptions> { new() { Price = priceId } },
            Discounts = couponId is not null
                ? new List<SubscriptionDiscountOptions> { new() { Coupon = couponId } }
                : null,
        };

        Stripe.Subscription sub = await subscriptionService.CreateAsync(options, null, ct);
        DateTime periodEnd = sub.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd
                             ?? DateTime.UtcNow.AddMonths(1);
        return (sub.Id, periodEnd);
    }

    public async Task<DateTime> ChangeSubscriptionPriceAsync(
        string stripeSubscriptionId, string newPriceId, CancellationToken ct)
    {
        Stripe.Subscription sub = await subscriptionService.GetAsync(stripeSubscriptionId, null, null, ct);
        string itemId = sub.Items.Data.First().Id;

        SubscriptionUpdateOptions options = new()
        {
            Items = new List<SubscriptionItemOptions> { new() { Id = itemId, Price = newPriceId } },
            // Upgrade: invoice and charge the prorated difference immediately,
            // so the studio sees exactly what the switch costs at the moment of upgrade.
            ProrationBehavior = "always_invoice",
        };

        Stripe.Subscription updated = await subscriptionService.UpdateAsync(stripeSubscriptionId, options, null, ct);
        return updated.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd
               ?? DateTime.UtcNow.AddMonths(1);
    }

    public async Task ScheduleSubscriptionPriceChangeAsync(
        string stripeSubscriptionId, string currentPriceId, string newPriceId,
        string newPriceInterval, CancellationToken ct)
    {
        // Wrap the live subscription in a schedule, keep the current phase untouched,
        // and append one phase on the new price starting at the current period end.
        SubscriptionSchedule schedule = await scheduleService.CreateAsync(
            new SubscriptionScheduleCreateOptions { FromSubscription = stripeSubscriptionId }, null, ct);

        SubscriptionSchedulePhase currentPhase = schedule.Phases.First();

        SubscriptionScheduleUpdateOptions options = new()
        {
            // After the new-price phase completes, release the subscription back to
            // normal (un-scheduled) billing on the new price.
            EndBehavior = "release",
            Phases = new List<SubscriptionSchedulePhaseOptions>
            {
                new()
                {
                    Items     = new List<SubscriptionSchedulePhaseItemOptions> { new() { Price = currentPriceId, Quantity = 1 } },
                    StartDate = currentPhase.StartDate,
                    EndDate   = currentPhase.EndDate,
                },
                new()
                {
                    Items    = new List<SubscriptionSchedulePhaseItemOptions> { new() { Price = newPriceId, Quantity = 1 } },
                    Duration = new SubscriptionSchedulePhaseDurationOptions
                    {
                        Interval      = newPriceInterval,
                        IntervalCount = 1,
                    },
                },
            },
        };

        await scheduleService.UpdateAsync(schedule.Id, options, null, ct);
    }

    public async Task CancelScheduledPriceChangeAsync(string stripeSubscriptionId, CancellationToken ct)
    {
        Stripe.Subscription sub = await subscriptionService.GetAsync(stripeSubscriptionId, null, null, ct);
        if (sub.ScheduleId is null) return; // nothing scheduled — idempotent

        // Releasing detaches the schedule and leaves the subscription running unchanged.
        await scheduleService.ReleaseAsync(sub.ScheduleId, null, null, ct);
    }
}
