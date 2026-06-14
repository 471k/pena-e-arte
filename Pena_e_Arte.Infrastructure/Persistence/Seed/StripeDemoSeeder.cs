using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Infrastructure.Persistence.Seed;

/// <summary>
/// Provisions REAL Stripe test-mode objects so one demo studio is genuinely card-billed
/// end-to-end — its owner can use the self-service "Change plan" flow, which calls the
/// Stripe API with real subscription/price ids.
///
/// Stripe price/subscription ids are account-specific, so they cannot be hardcoded in
/// seed data; they must be created against the configured Stripe account at runtime.
/// This step is:
///   • Opt-in   — runs only when a "sk_test_" key is configured (never touches live Stripe).
///   • Idempotent — reuses existing prices/subscription; safe to run on every startup.
///   • Non-fatal  — any Stripe error is logged and the studio simply stays cash-billed.
/// </summary>
public static class StripeDemoSeeder
{
    // Ink & Soul Studio — the seeded Active studio used for the card-billed demo.
    private static readonly Guid DemoStudioId = new("bbbb0001-0000-0000-0000-000000000000");

    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        using IServiceScope scope = services.CreateScope();
        ILogger logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>().CreateLogger("StripeDemoSeeder");

        string? key = config["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("sk_test_"))
        {
            logger.LogInformation(
                "Stripe test key not configured — demo studio stays cash-billed (no Stripe provisioning).");
            return;
        }

        try
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            Stripe.ProductService       products       = new();
            Stripe.PriceService         prices         = new();
            Stripe.CustomerService      customers      = new();
            Stripe.PaymentMethodService paymentMethods = new();
            Stripe.SubscriptionService  subscriptions  = new();

            // 1. Every plan gets real monthly + yearly Stripe prices, so any plan-change
            //    target resolves to a real price id.
            List<Plan> plans = await db.Plans.ToListAsync();
            foreach (Plan plan in plans)
            {
                plan.StripePriceIdMonthly = await EnsurePriceAsync(
                    prices, plan, "month", plan.PriceMonthly, plan.StripePriceIdMonthly);
                plan.StripePriceIdYearly = await EnsurePriceAsync(
                    prices, plan, "year", plan.PriceYearly, plan.StripePriceIdYearly);
            }
            await db.SaveChangesAsync();

            // 2. The demo studio gets a real active subscription on its current plan.
            Studio? studio = await db.Studios
                .Include(s => s.Subscription)
                .FirstOrDefaultAsync(s => s.Id == DemoStudioId);

            if (studio?.Subscription is null)
            {
                logger.LogWarning("Demo studio/subscription not found — skipping Stripe subscription provisioning.");
                return;
            }

            Subscription sub = studio.Subscription;

            if (!string.IsNullOrEmpty(sub.StripeSubscriptionId)
                && await SubscriptionLiveAsync(subscriptions, sub.StripeSubscriptionId))
            {
                logger.LogInformation("Demo studio already card-billed (subscription {Id}).", sub.StripeSubscriptionId);
                return;
            }

            Plan? currentPlan = plans.FirstOrDefault(p => p.Id == sub.PlanId) ?? plans.FirstOrDefault();
            if (currentPlan?.StripePriceIdMonthly is null)
            {
                logger.LogWarning("No plan price available — cannot provision a demo subscription.");
                return;
            }

            // Reuse the studio's customer, or create one with a test card as default PM
            // so the first invoice is paid immediately and the subscription becomes active.
            string customerId = studio.StripeCustomerId
                ?? (await customers.CreateAsync(new Stripe.CustomerCreateOptions
                {
                    Email = studio.OwnerEmail,
                    Name  = studio.Name,
                })).Id;

            Stripe.PaymentMethod pm = await paymentMethods.AttachAsync(
                "pm_card_visa", new Stripe.PaymentMethodAttachOptions { Customer = customerId });
            await customers.UpdateAsync(customerId, new Stripe.CustomerUpdateOptions
            {
                InvoiceSettings = new Stripe.CustomerInvoiceSettingsOptions { DefaultPaymentMethod = pm.Id },
            });

            Stripe.Subscription stripeSub = await subscriptions.CreateAsync(new Stripe.SubscriptionCreateOptions
            {
                Customer = customerId,
                Items    = new List<Stripe.SubscriptionItemOptions> { new() { Price = currentPlan.StripePriceIdMonthly } },
            });

            studio.StripeCustomerId  = customerId;
            sub.StripeSubscriptionId = stripeSub.Id;
            sub.PlanId               = currentPlan.Id;
            sub.Status               = SubscriptionStatus.Active;
            sub.CurrentPeriodEnd     = stripeSub.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd
                                       ?? DateTime.UtcNow.AddMonths(1);
            await db.SaveChangesAsync();

            logger.LogInformation("Demo studio is now card-billed (subscription {Id}).", stripeSub.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Stripe demo provisioning failed — demo studio stays cash-billed.");
        }
    }

    private static async Task<string> EnsurePriceAsync(
        Stripe.PriceService prices, Plan plan, string interval, decimal amount, string? existing)
    {
        // Already linked and still present at Stripe — keep it.
        if (!string.IsNullOrEmpty(existing) && existing.StartsWith("price_")
            && await PriceExistsAsync(prices, existing))
            return existing;

        // Fallback: a matching price may already exist from a previous DB reset.
        try
        {
            Stripe.StripeSearchResult<Stripe.Price> found = await prices.SearchAsync(new Stripe.PriceSearchOptions
            {
                Query = $"active:'true' AND metadata['plan_id']:'{plan.Id}' AND metadata['interval']:'{interval}'",
            });
            if (found.Data.Count > 0) return found.Data[0].Id;
        }
        catch (Stripe.StripeException) { /* search not critical — fall through to create */ }

        Stripe.Price price = await prices.CreateAsync(new Stripe.PriceCreateOptions
        {
            UnitAmount  = (long)(amount * 100),
            Currency    = "eur",
            Recurring   = new Stripe.PriceRecurringOptions { Interval = interval },
            ProductData = new Stripe.PriceProductDataOptions { Name = $"{plan.Name} ({interval}ly)" },
            Metadata    = new Dictionary<string, string>
            {
                ["plan_id"]  = plan.Id.ToString(),
                ["interval"] = interval,
            },
        });
        return price.Id;
    }

    private static async Task<bool> PriceExistsAsync(Stripe.PriceService prices, string id)
    {
        try { await prices.GetAsync(id); return true; }
        catch (Stripe.StripeException) { return false; }
    }

    private static async Task<bool> SubscriptionLiveAsync(Stripe.SubscriptionService subs, string id)
    {
        try
        {
            Stripe.Subscription s = await subs.GetAsync(id);
            return s.Status is "active" or "trialing" or "past_due";
        }
        catch (Stripe.StripeException) { return false; }
    }
}
