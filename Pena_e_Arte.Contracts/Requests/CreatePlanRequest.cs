namespace Pena_e_Arte.Contracts.Requests;

public record CreatePlanRequest(
    string Name,
    int YearlyDiscountPercent,
    List<PlanPriceRequest> Prices,
    int? MaxArtists = null,
    int? MaxAppointmentsPerMonth = null,
    int? MaxNotificationsPerMonth = null,
    int? MaxStorageGb = null,
    int? MaxLocations = null,
    bool AllowApiAccess = false,
    bool PrioritySupport = false,
    bool AllowBrandingRemoval = false);
