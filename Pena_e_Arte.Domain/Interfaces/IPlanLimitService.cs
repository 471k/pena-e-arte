using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// One usage dimension: the current count/amount versus the Plan's cap.
/// Current is a double (not long) so all five dimensions share one shape without
/// awkward casting — StorageGb needs fractional precision (rounded to 1 decimal),
/// so every other dimension uses the same type rather than mixing long and double.
/// Max is null when the Plan places no cap on this dimension (unlimited).
/// </summary>
public sealed record PlanUsageDimension(double Current, int? Max);

/// <summary>
/// A snapshot of the current tenant's usage across all five Plan-capped dimensions.
/// Mirrors how SubscriptionSnapshot lives alongside ISubscriptionAccessService.
/// </summary>
public sealed record PlanUsageSnapshot(
    string PlanName,
    PlanUsageDimension Artists,
    PlanUsageDimension AppointmentsPerMonth,
    PlanUsageDimension NotificationsPerMonth,
    PlanUsageDimension StorageGb,
    PlanUsageDimension Locations);

/// <summary>
/// Checks the current tenant's usage against its Plan's limits for a given dimension.
/// Throws PlanLimitExceededException when the studio is at or over its cap. A null
/// Max* field on the resolved Plan means unlimited — no exception, no check performed.
/// </summary>
public interface IPlanLimitService
{
    Task EnsureWithinLimitAsync(QuotaType quotaType, CancellationToken ct = default);

    /// <summary>
    /// Same check as <see cref="EnsureWithinLimitAsync(QuotaType, CancellationToken)"/> but
    /// scoped to an explicit studio rather than ICurrentTenant — for the one legitimate case
    /// where a caller's JWT still carries a different active tenant than the studio being
    /// checked (accepting a cross-studio StudioJoinInvite before the tenant claim has switched;
    /// see AcceptStudioJoinInviteCommand). Ordinary quota-checked commands should keep using the
    /// ICurrentTenant-scoped overload via PlanLimitBehavior — do not force-fit this overload into
    /// the IQuotaCheckedCommand pipeline.
    /// </summary>
    Task EnsureWithinLimitAsync(Guid studioId, QuotaType quotaType, CancellationToken ct = default);

    /// <summary>
    /// Returns current usage vs. cap for all five dimensions, or null if the studio has
    /// no resolvable Plan (e.g. mid-trial before a plan is chosen).
    /// </summary>
    Task<PlanUsageSnapshot?> GetUsageSnapshotAsync(CancellationToken ct = default);

    /// <summary>
    /// Invalidates the cached usage count for one dimension immediately — call this after a
    /// quota-checked command's write succeeds so the very next check reflects reality instead
    /// of waiting up to the cache TTL. Narrows but does not eliminate the race between two
    /// truly concurrent requests — see PlanLimitService's cache-logic comment.
    /// </summary>
    Task InvalidateUsageCacheAsync(QuotaType quotaType, CancellationToken ct = default);

    /// <summary>
    /// Same as <see cref="InvalidateUsageCacheAsync(QuotaType, CancellationToken)"/> but scoped
    /// to an explicit studio rather than ICurrentTenant — the write-through counterpart to
    /// <see cref="EnsureWithinLimitAsync(Guid, QuotaType, CancellationToken)"/> for the same
    /// cross-studio-accept case. Using the ICurrentTenant-scoped overload there would invalidate
    /// the caller's OLD studio's cache (their active tenant at that point in the flow) instead of
    /// the studio that actually gained the new artist.
    /// </summary>
    Task InvalidateUsageCacheAsync(Guid studioId, QuotaType quotaType, CancellationToken ct = default);
}
