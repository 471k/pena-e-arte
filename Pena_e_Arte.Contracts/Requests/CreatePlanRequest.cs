namespace Pena_e_Arte.Contracts.Requests;

public record CreatePlanRequest(
    string  Name,
    string  BillingInterval,
    decimal PriceMonthly,
    decimal PriceYearly,
    int     YearlyDiscountPercent,
    string? StripePriceIdMonthly = null,
    string? StripePriceIdYearly  = null);
