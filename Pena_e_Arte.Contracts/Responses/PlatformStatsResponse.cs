namespace Pena_e_Arte.Contracts.Responses;

public record PlatformStatsResponse(
    int     TotalStudios,
    int     ActiveSubscriptions,
    int     TrialStudios,
    int     SuspendedStudios,
    decimal MonthlyRecurringRevenue,
    int     TotalReferralCodes,
    int     ActiveReferralCodes);
