namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Data-retention windows, in days. Every value here is a PLACEHOLDER pending founder +
/// data-protection-lawyer input (see docs/engineering/EPIC-0001 open question §3.6 and
/// docs/payments/implementation-readiness.md §9). Engineering's job is making these
/// configurable, not choosing the final numbers — do NOT treat these defaults as final.
/// </summary>
public class RetentionOptions
{
    public const string Section = "App:RetentionDays";

    /// <summary>Days after signing before a consent form is soft-deleted. PLACEHOLDER (2 years).</summary>
    public int ConsentForms { get; init; } = 730;

    /// <summary>Days before body-map data is purged. PLACEHOLDER (2 years).</summary>
    public int BodyMaps { get; init; } = 730;

    /// <summary>Days a soft-deleted row is retained before the permanent hard purge. PLACEHOLDER.</summary>
    public int GracePeriodBeforeHardPurge { get; init; } = 30;
}
