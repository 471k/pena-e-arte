namespace Pena_e_Arte.Contracts.Responses;

public record StudioPlanUsageRow(
    Guid    StudioId, string StudioName, string PlanName,
    int     ArtistCount, int? MaxArtists,
    int     AppointmentsThisMonth, int? MaxAppointmentsPerMonth,
    int     NotificationsThisMonth, int? MaxNotificationsPerMonth,
    double  StorageGbUsed, int? MaxStorageGb);

public record PlanUsageReportResponse(List<StudioPlanUsageRow> Studios);
