namespace Pena_e_Arte.Domain.Services;

/// <summary>
/// Where one area of a studio's setup stands (E05a, O02). Three states, not two: a default that
/// registration created is neither missing nor proof the owner looked at it.
/// </summary>
public enum SetupAreaState
{
    /// <summary>Nothing exists for this area yet.</summary>
    NotConfigured,

    /// <summary>Registration filled this in for the owner, and the owner hasn't confirmed it.</summary>
    DefaultUnreviewed,

    /// <summary>The owner set this up, or confirmed the default.</summary>
    Reviewed,
}

/// <summary>
/// The setup checklist's derivation, kept pure so every state is unit-testable. Two areas can be in
/// <see cref="SetupAreaState.DefaultUnreviewed"/>: opening hours (registration seeds a Mon–Fri
/// schedule) and the artist pay model (every studio starts on Commission with no rate, A07a).
/// Registration never creates an artist or a deposit rule, so one existing means the owner made it.
/// </summary>
public static class SetupReadiness
{
    /// <summary>
    /// Areas whose rows only the owner can create (artists, deposit rules): there is no seeded
    /// default to tell apart, so existence is the honest measure.
    /// </summary>
    public static SetupAreaState OwnerCreated(bool anyExist) =>
        anyExist ? SetupAreaState.Reviewed : SetupAreaState.NotConfigured;

    /// <summary>
    /// Opening hours. No rows is not configured whatever else is true. Rows with no confirmation are
    /// registration's default. Once confirmed they stay reviewed, including after later edits, because
    /// every edit goes through the hours form and confirms them again.
    /// </summary>
    public static SetupAreaState Hours(bool anyHoursRows, DateTime? hoursReviewedAt) =>
        !anyHoursRows ? SetupAreaState.NotConfigured
        : hoursReviewedAt is null ? SetupAreaState.DefaultUnreviewed
        : SetupAreaState.Reviewed;

    /// <summary>
    /// How artists get paid (A07a). Never <see cref="SetupAreaState.NotConfigured"/>: unlike hours, a
    /// studio always has an arrangement (Commission from registration), it just may not be reviewed.
    /// Reviewed once the owner saved the pay-model form, which refuses terms that can't give a share,
    /// so Reviewed always means a share can be worked out. Whether the studio has artists other than
    /// the owner doesn't change the state: a solo owner sets it in one step, and it applies to them as
    /// soon as anyone joins.
    /// </summary>
    public static SetupAreaState PayModel(DateTime? payModelReviewedAt) =>
        payModelReviewedAt is null ? SetupAreaState.DefaultUnreviewed : SetupAreaState.Reviewed;
}
