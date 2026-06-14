namespace Pena_e_Arte.Contracts.Responses;

public record PlatformStatsResponse(
    int     TotalStudios,
    int     ActiveSubscriptions,
    int     TrialStudios,
    int     GracePeriodStudios,
    decimal Mrr,
    double  TrialConversionRate,
    int     NewStudiosThisMonth);
