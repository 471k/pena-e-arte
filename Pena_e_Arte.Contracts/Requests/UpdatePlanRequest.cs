namespace Pena_e_Arte.Contracts.Requests;

public record UpdatePlanRequest(
    string  Name,
    decimal PriceMonthly,
    decimal PriceYearly,
    int     YearlyDiscountPercent,
    bool    AllowBrandingRemoval     = false,
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
