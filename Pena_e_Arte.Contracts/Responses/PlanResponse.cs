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
    List<PlanPriceResponse> Prices);
