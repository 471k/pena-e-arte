namespace Pena_e_Arte.Contracts.Responses.Public;

/// <summary>One purchasable billing cadence of a tier. Price is in the response's Currency.</summary>
public record PublicPlanPriceResponse(string Interval, decimal Price);

/// <summary>
/// Anonymous, marketing-page view of a subscription tier. Deliberately a separate, smaller
/// shape than the owner/admin <c>PlanResponse</c>: no plan id, no Stripe price ids, no
/// subscriber count and no <c>PrioritySupport</c> (unimplemented, hidden from every UI).
/// A null limit means unlimited.
/// </summary>
public record PublicPlanResponse(
    string Name,
    string Currency,
    List<PublicPlanPriceResponse> Prices,
    // Computed from the active Monthly/Yearly prices — never from Plan.YearlyDiscountPercent.
    // Null when either price is missing or the yearly price does not save against monthly.
    decimal? YearlyMonthsFree,
    bool AllowBrandingRemoval,
    bool AllowMarketingCampaigns,
    bool AllowApiAccess,
    int? MaxArtists,
    int? MaxAppointmentsPerMonth,
    int? MaxNotificationsPerMonth,
    int? MaxStorageGb,
    int? MaxLocations);
