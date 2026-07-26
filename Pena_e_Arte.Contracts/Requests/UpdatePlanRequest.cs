namespace Pena_e_Arte.Contracts.Requests;

public record UpdatePlanRequest(
    string Name,
    int YearlyDiscountPercent,
    List<PlanPriceRequest> Prices,
    bool AllowBrandingRemoval = false,
    int? MaxArtists = null,
    int? MaxAppointmentsPerMonth = null,
    int? MaxNotificationsPerMonth = null,
    int? MaxStorageGb = null,
    int? MaxLocations = null,
    bool AllowApiAccess = false,
    bool PrioritySupport = false);
