namespace Pena_e_Arte.Contracts.Responses;

public record PlanResponse(
    Guid    Id,
    string  Name,
    string  BillingInterval,
    decimal PriceMonthly,
    decimal PriceYearly,
    int     YearlyDiscountPercent,
    string? StripePriceIdMonthly,
    string? StripePriceIdYearly);
