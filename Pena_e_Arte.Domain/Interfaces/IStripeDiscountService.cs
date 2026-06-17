namespace Pena_e_Arte.Domain.Interfaces;

public interface IStripeDiscountService
{
    Task<string> CreateOneMonthFreeCouponAsync(string idempotencyKey, CancellationToken ct);
}
