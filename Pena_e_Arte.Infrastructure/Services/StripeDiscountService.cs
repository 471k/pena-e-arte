using Pena_e_Arte.Domain.Interfaces;
using Stripe;

namespace Pena_e_Arte.Infrastructure.Services;

public class StripeDiscountService(CouponService couponService) : IStripeDiscountService
{
    public async Task<string> CreateOneMonthFreeCouponAsync(CancellationToken ct)
    {
        CouponCreateOptions options = new()
        {
            PercentOff       = 100,
            Duration         = "repeating",
            DurationInMonths = 1,
        };

        Coupon coupon = await couponService.CreateAsync(options, null, ct);
        return coupon.Id;
    }
}
