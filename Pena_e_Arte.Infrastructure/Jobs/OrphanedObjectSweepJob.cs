using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Finds stored objects that no row points at (docs/claude/overnight-prompt-orphaned-object-cleanup-2026-11-15.md).
/// Every user-initiated "remove" deletes the row and leaves the object, and only two prefixes are ever
/// cleaned, so removed files stay in the bucket, stay fetchable and keep counting against the studio's
/// storage quota (StorageReconciliationJob sums the bucket, not the rows).
///
/// **REPORT-ONLY. This job deletes nothing.** It has no delete call at all; deletion is Phase 3, behind
/// configuration, and waits for Desktop to read a staging and a production run, then Phi's go.
///
/// A sweep, not a delete at each remove site, because objects are shared between rows (the catalog
/// design is one object in three rows) and a sweep also recovers what is already orphaned. Same shape as
/// GuestPendingUploadCleanupJob: list, keep only objects older than a minimum age, ask which keys are
/// referenced, act on the rest.
///
/// Order matters: the bucket is listed FIRST and the references read AFTER, so a row saved between the
/// two can only protect more. The minimum age covers the other race: an object is uploaded before its
/// row is saved.
///
/// Excluded prefixes are never candidates: consent/ (RetentionPurgeJob deletes it on a legal retention
/// clock that "unreferenced" would race) and reports/industry/ (read by key, no row ever points at it).
/// appointments/guest-pending/ is deliberately NOT excluded: GuestPendingUploadCleanupJob applies the same
/// unreferenced test at 48 hours, so it always acts first and this sweep is a slower net behind it.
///
/// Logs counts, bytes, studio ids and area names only, never a key or a URL.
/// </summary>
public class OrphanedObjectSweepJob(
    IR2Service r2,
    StoredObjectReferenceReader references,
    TimeProvider clock,
    ILogger<OrphanedObjectSweepJob> logger)
{
    /// <summary>
    /// Longer than GuestPendingUploadCleanupJob's 48 hours on purpose: it must always act first on its own
    /// prefix, and a booking or revision form left open over a weekend must not lose its upload.
    /// </summary>
    internal static readonly TimeSpan MinimumAge = TimeSpan.FromDays(7);

    internal static readonly string[] ExcludedPrefixes = ["consent/", "reports/industry/"];

    public async Task RunAsync(CancellationToken ct = default)
    {
        IReadOnlyList<R2ObjectInfo> listed = await r2.ListByPrefixAsync("", ct);
        StoredObjectReferences refs = await references.ReadAsync(ct);

        DateTime cutoff = clock.GetUtcNow().UtcDateTime - MinimumAge;
        int excluded = 0, tooYoung = 0, referenced = 0;
        Dictionary<(string Owner, string Area), (int Count, long Bytes)> orphans = [];

        foreach (R2ObjectInfo obj in listed)
        {
            if (ExcludedPrefixes.Any(p => obj.Key.StartsWith(p, StringComparison.Ordinal))) { excluded++; continue; }
            if (obj.LastModified >= cutoff) { tooYoung++; continue; }
            if (refs.IsReferenced(obj.Key)) { referenced++; continue; }

            (string Owner, string Area) group = Classify(obj.Key);
            orphans.TryGetValue(group, out (int Count, long Bytes) sum);
            orphans[group] = (sum.Count + 1, sum.Bytes + obj.SizeBytes);
        }

        foreach (KeyValuePair<(string Owner, string Area), (int Count, long Bytes)> g in
                 orphans.OrderBy(o => o.Key.Owner, StringComparer.Ordinal).ThenBy(o => o.Key.Area, StringComparer.Ordinal))
        {
            logger.LogInformation(
                "OrphanedObjectSweepJob (report-only) would delete {Count} object(s), {Bytes} bytes, owner {Owner}, area {Area}",
                g.Value.Count, g.Value.Bytes, g.Key.Owner, g.Key.Area);
        }

        logger.LogInformation(
            "OrphanedObjectSweepJob (report-only) listed {Listed} object(s): {Excluded} excluded, {TooYoung} younger than {MinimumAgeDays} days, " +
            "{Referenced} referenced, {Orphans} unreferenced ({OrphanBytes} bytes) that it would delete; deleted 0. " +
            "References: {Keys} key(s) from {Values} stored value(s), {OnNoKnownAddress} on no known address",
            listed.Count, excluded, tooYoung, (int)MinimumAge.TotalDays,
            referenced, orphans.Values.Sum(v => v.Count), orphans.Values.Sum(v => v.Bytes),
            refs.Keys.Count, refs.ValuesRead, refs.ValuesOnNoKnownAddress);
    }

    /// <summary>
    /// The studio a key belongs to (its first segment, when that is a studio id) and the area under it, for
    /// the report only. Keys outside any studio ("appointments/guest-pending/...") are owned by "platform".
    /// Never used to decide anything: the reference check is by whole key, against every column.
    /// </summary>
    internal static (string Owner, string Area) Classify(string key)
    {
        string[] parts = key.Split('/');
        if (parts.Length >= 2 && Guid.TryParse(parts[0], out Guid studioId))
            return (studioId.ToString(), parts.Length >= 3 ? parts[1] : "(root)");
        // Keys outside any studio: name the area by its first one or two folders, never the file name.
        return ("platform", parts.Length switch
        {
            1 => "(root)",
            2 => parts[0],
            _ => $"{parts[0]}/{parts[1]}",
        });
    }
}
