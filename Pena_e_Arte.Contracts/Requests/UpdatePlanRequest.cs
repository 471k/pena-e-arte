namespace Pena_e_Arte.Contracts.Requests;

public record UpdatePlanRequest(
    string  Name,
    decimal PriceMonthly,
    decimal PriceYearly,
    int     YearlyDiscountPercent,
    string? StripePriceIdMonthly = null,
    string? StripePriceIdYearly  = null);
