namespace Pena_e_Arte.Contracts.Responses;

public record PlanUsageDimensionResponse(double Current, int? Max);

public record PlanUsageResponse(
    string PlanName,
    PlanUsageDimensionResponse Artists,
    PlanUsageDimensionResponse AppointmentsPerMonth,
    PlanUsageDimensionResponse NotificationsPerMonth,
    PlanUsageDimensionResponse StorageGb,
    PlanUsageDimensionResponse Locations);
