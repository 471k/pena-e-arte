namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Data-retention windows, in days. ConsentForms/BodyMaps are founder-confirmed (7 years,
/// the body-art record-retention convention); GracePeriodBeforeHardPurge is the confirmed
/// 30-day soft-delete grace. All configurable via App:RetentionDays.
/// </summary>
public class RetentionOptions
{
    public const string Section = "App:RetentionDays";

    /// <summary>Days after signing before a consent form is soft-deleted. 7 years —
    /// confirmed by founder 2026-08-01 (body-art record-retention convention).</summary>
    public int ConsentForms { get; init; } = 2555;

    /// <summary>Days before body-map data is soft-deleted. 7 years — confirmed by founder
    /// 2026-08-01 (body-art record-retention convention).</summary>
    public int BodyMaps { get; init; } = 2555;

    /// <summary>Days a soft-deleted row is retained before the permanent hard purge.
    /// 30 days — confirmed by founder 2026-08-01.</summary>
    public int GracePeriodBeforeHardPurge { get; init; } = 30;
}
