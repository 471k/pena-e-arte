namespace Pena_e_Arte.Contracts.Requests;

public record CreatePlanRequest(
    string  Name,
    string  BillingInterval,
    decimal PriceMonthly,
    decimal PriceYearly,
    int     YearlyDiscountPercent,
    string? StripePriceIdMonthly     = null,
    string? StripePriceIdYearly      = null,
    int?    MaxArtists               = null,
    int?    MaxAppointmentsPerMonth  = null,
    int?    MaxNotificationsPerMonth = null,
    int?    MaxStorageGb             = null,
    int?    MaxLocations             = null,
    bool    AllowApiAccess           = false,
    bool    PrioritySupport          = false,
    Guid?   PairedPlanId             = null);
