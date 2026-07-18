namespace Pena_e_Arte.Contracts.Responses;

public record PlanResponse(
    Guid    Id,
    string  Name,
    string  BillingInterval,
    decimal PriceMonthly,
    decimal PriceYearly,
    int     YearlyDiscountPercent,
    bool    AllowBrandingRemoval,
    string? StripePriceIdMonthly,
    string? StripePriceIdYearly,
    int     SubscriberCount,
    int?    MaxArtists,
    int?    MaxAppointmentsPerMonth,
    int?    MaxNotificationsPerMonth,
    int?    MaxStorageGb,
    int?    MaxLocations,
    bool    AllowApiAccess,
    bool    PrioritySupport,
    Guid?   PairedPlanId);
