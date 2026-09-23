using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Billing;

/// <summary>
/// D6 — a referral reward is always worth one month of the tier's monthly price,
/// regardless of which interval the referred/referrer studio is actually billed on.
/// See architecture.md Decisions Log — "Referral coupon fix".
/// </summary>
public static class ReferralRewardAmount
{
    public static decimal OneMonthOf(Plan plan)
    {
        PlanPrice? monthly = plan.Prices.FirstOrDefault(p => p.Interval == BillingInterval.Monthly);
        if (monthly is not null) return monthly.Price;

        PlanPrice? yearly = plan.Prices.FirstOrDefault(p => p.Interval == BillingInterval.Yearly);
        return yearly is not null ? Math.Round(yearly.Price / 12m, 2) : 0m;
    }
}
