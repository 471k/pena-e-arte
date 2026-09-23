using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// A referral reward is always worth one month of the tier's monthly price (D6, see
/// architecture.md Decisions Log — "Referral coupon fix"). <paramref name="StripePriceId"/>
/// is required for Yearly (used to look up the price's currency) and unused for Monthly.
/// </summary>
public sealed record ReferralCouponRequest(
    BillingInterval Interval, string? StripePriceId, decimal MonthlyPrice, string IdempotencyKey);

public interface IStripeDiscountService
{
    /// <summary>
    /// Monthly: 100% off, repeating for 1 month (unchanged behaviour — a monthly
    /// subscription has one invoice in that window, so this is genuinely one month free).
    /// Yearly: a fixed amount-off coupon worth <see cref="ReferralCouponRequest.MonthlyPrice"/>,
    /// applied once — a repeating 100%-off coupon on a yearly price would zero the whole
    /// first year (the bug this replaces).
    /// </summary>
    Task<string> CreateReferralCouponAsync(ReferralCouponRequest request, CancellationToken ct);
}
