namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// Platform-wide aggregate statistics. All counts are point-in-time snapshots.
/// TotalStudios = ActiveSubscriptions + TrialStudios + GracePeriodStudios
///              + PastDueStudios + CancelledStudios
///              (every active studio falls into exactly one bucket).
/// SuspendedStudios are excluded from TotalStudios — they are deactivated by the issuer.
/// </summary>
public record PlatformStatsResponse(
    int     TotalStudios,
    int     ActiveSubscriptions,
    int     TrialStudios,
    int     GracePeriodStudios,
    int     PastDueStudios,
    int     CancelledStudios,
    int     SuspendedStudios,
    decimal Mrr,
    double  MrrGrowthPercent,
    double  TrialConversionRate,
    int     NewStudiosThisMonth);
