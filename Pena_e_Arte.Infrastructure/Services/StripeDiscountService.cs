using Pena_e_Arte.Domain.Interfaces;
using Stripe;

namespace Pena_e_Arte.Infrastructure.Services;

public class StripeDiscountService(CouponService couponService) : IStripeDiscountService
{
    public async Task<string> CreateOneMonthFreeCouponAsync(string idempotencyKey, CancellationToken ct)
    {
        CouponCreateOptions options = new()
        {
            PercentOff       = 100,
            Duration         = "repeating",
            DurationInMonths = 1,
        };

        RequestOptions requestOptions = new() { IdempotencyKey = idempotencyKey };
        Coupon coupon = await couponService.CreateAsync(options, requestOptions, ct);
        return coupon.Id;
    }
}
