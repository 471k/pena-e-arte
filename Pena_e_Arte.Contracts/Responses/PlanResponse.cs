namespace Pena_e_Arte.Contracts.Responses;

public record PlanResponse(
    Guid Id,
    string Name,
    int YearlyDiscountPercent,
    bool AllowBrandingRemoval,
    int SubscriberCount,
    int? MaxArtists,
    int? MaxAppointmentsPerMonth,
    int? MaxNotificationsPerMonth,
    int? MaxStorageGb,
    int? MaxLocations,
    bool AllowApiAccess,
    bool PrioritySupport,
    bool AllowMarketingCampaigns,
    List<PlanPriceResponse> Prices,
    // Computed from the active Monthly/Yearly PlanPrice rows (D7) — never from
    // YearlyDiscountPercent, which is admin-input-only after this. Null when either row
    // is missing/inactive, Monthly is 0, or the computed saving is <= 0.
    decimal? YearlySavingAmount,
    decimal? YearlyMonthsFree,
    // MrrRules.PlatformCurrency, uppercased — the one currency this platform bills in (no
    // multi-currency support anywhere). Added so every frontend surface that prices a plan
    // (owner Subscribe/Billing pages, admin Plan editor, the public Pricing page's
    // PublicPlanResponse) reads currency from the API instead of each hardcoding its own —
    // see architecture.md Decisions Log, "Plan currency — one source of truth".
    string Currency);
