using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Stripe;

namespace Pena_e_Arte.Infrastructure.Services;

public class StripeDiscountService(CouponService couponService, PriceService priceService) : IStripeDiscountService
{
    public async Task<string> CreateReferralCouponAsync(ReferralCouponRequest request, CancellationToken ct)
    {
        CouponCreateOptions options = request.Interval == BillingInterval.Monthly
            ? new CouponCreateOptions
            {
                PercentOff = 100,
                Duration = "repeating",
                DurationInMonths = 1,
            }
            : await BuildYearlyCouponOptionsAsync(request, ct);

        RequestOptions requestOptions = new() { IdempotencyKey = request.IdempotencyKey };
        Coupon coupon = await couponService.CreateAsync(options, requestOptions, ct);
        return coupon.Id;
    }

    // A yearly subscription has exactly one invoice inside a "repeating, 1 month" window,
    // so the Monthly branch's 100%-off coupon would make the whole first year free — the
    // bug this fixes (D6). A fixed amount-off, applied once, is worth one month regardless
    // of billing interval.
    private async Task<CouponCreateOptions> BuildYearlyCouponOptionsAsync(
        ReferralCouponRequest request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.StripePriceId))
            throw new InvalidOperationException(
                "Cannot create a Yearly referral coupon without a linked Stripe price.");

        Price price = await priceService.GetAsync(request.StripePriceId, null, null, ct);

        return new CouponCreateOptions
        {
            AmountOff = (long)Math.Round(request.MonthlyPrice * 100m),
            Currency = price.Currency,
            Duration = "once",
            Name = "Referral: 1 month free",
        };
    }
}
