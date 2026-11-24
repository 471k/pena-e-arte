using System.Globalization;
using Amazon.S3;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Finds stored objects that no row points at (docs/claude/overnight-prompt-orphaned-object-cleanup-2026-11-15.md).
/// Every user-initiated "remove" deletes the row and leaves the object, and only two prefixes are ever
/// cleaned, so removed files stay in the bucket, stay fetchable and keep counting against the studio's
/// storage quota (StorageReconciliationJob sums the bucket, not the rows).
///
/// **Two modes, report-only by default** (OrphanedObjectSweepOptions). Report-only logs what it would act on
/// and touches nothing. Quarantine MOVES each unreferenced object to _quarantine/{yyyy-MM-dd}/{original key}:
/// a server-side copy, a size check on the copy, and only then a delete of the original. A failed copy or a
/// copy that doesn't verify leaves the original where it was. Nothing is ever hard-deleted here: emptying the
/// quarantine is Phase 4, a separate decision. Restoring is in docs/infra/orphan-quarantine-runbook.md.
///
/// _quarantine/ is TOP-LEVEL on purpose: the presign route lets a caller choose any folder under
/// {studioId}/, so {studioId}/_quarantine/ would be a quarantine users can write into. It is skipped at the
/// list call (IR2Service.ListAllExceptAsync), so the next run never sees, re-condemns or pays memory for it.
/// A bucket lock rule on that prefix keeps quarantined objects for 14 days, and a lifecycle rule deletes them at
/// 15 (the lifecycle must exceed the lock, or the expiry is refused). Both are Cloudflare bucket settings, not
/// something this job sets or checks, and no person is needed to empty the quarantine (Phi, 23 Nov).
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
/// The backup bucket needs no exclusion: every R2Service call pins BucketName to the primary bucket, so the
/// backup is unreachable from this service at all. Structural, not configured.
///
/// LastModified arrives as DateTimeKind.Utc (R2Service.ToUtc) and the cutoff is UTC, so the age comparison
/// does not depend on the pod's time zone. An object exactly at the cutoff is still too young.
///
/// Logs counts, bytes, studio ids and area names only, never a key or a URL.
/// </summary>
/// <remarks>
/// Retries are off ([AutomaticRetry(Attempts = 0)]): a refused run and a run where every move fails both throw,
/// and Hangfire's default of 10 retries would repeat each one. One failure lands in Failed, where the Hangfire
/// alert sees it. A daily, idempotent sweep loses nothing by skipping a retry: tomorrow lists the same objects.
/// </remarks>
[AutomaticRetry(Attempts = 0)]
public class OrphanedObjectSweepJob(
    IR2Service r2,
    StoredObjectReferenceReader references,
    TimeProvider clock,
    IOptions<OrphanedObjectSweepOptions> options,
    ILogger<OrphanedObjectSweepJob> logger)
{
    /// <summary>
    /// Two constraints: longer than GuestPendingUploadCleanupJob's 48 hours, so that job always acts first on
    /// its own prefix; and longer than the longest time a form holding an upload sits unsaved (a booking
    /// abandoned on a Friday and finished on a Monday). Change it only with both in mind.
    /// </summary>
    internal static readonly TimeSpan MinimumAge = TimeSpan.FromDays(7);

    internal static readonly string[] ExcludedPrefixes = ["consent/", "reports/industry/"];

    /// <summary>Top-level, never under a studio id: see the class comment.</summary>
    internal const string QuarantineFolder = "_quarantine/";

    /// <summary>Never listed at all, unlike ExcludedPrefixes, which are listed and counted as "excluded".</summary>
    internal static readonly string[] NotListedTopLevelFolders = [QuarantineFolder];

    /// <summary>
    /// The quarantine run refuses to move anything when more than this share of the candidates (old enough and
    /// not excluded) looks unreferenced, once there are at least RefusalMinimumCount of them. A reference
    /// reader that silently reads too little would otherwise condemn live files in bulk. The move is
    /// reversible, so this is a brake, not the only protection. The minimum keeps a small bucket (production
    /// holds 6 objects) from tripping it on ordinary numbers.
    /// </summary>
    internal const double RefusalShare = 0.5;
    internal const int RefusalMinimumCount = 20;

    public async Task RunAsync(CancellationToken ct = default)
    {
        bool quarantine = ResolveQuarantineMode();

        IReadOnlyList<R2ObjectInfo> listed = await r2.ListAllExceptAsync(NotListedTopLevelFolders, ct);
        StoredObjectReferences refs = await references.ReadAsync(ct);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        DateTime cutoff = now - MinimumAge;
        int excluded = 0, tooYoung = 0, referenced = 0;
        List<(R2ObjectInfo Object, (string Owner, string Area) Group)> orphans = [];

        foreach (R2ObjectInfo obj in listed)
        {
            if (ExcludedPrefixes.Any(p => obj.Key.StartsWith(p, StringComparison.Ordinal))) { excluded++; continue; }
            if (obj.LastModified >= cutoff) { tooYoung++; continue; }
            if (refs.IsReferenced(obj.Key)) { referenced++; continue; }
            orphans.Add((obj, Classify(obj.Key)));
        }

        if (!quarantine)
        {
            ReportOnly(listed.Count, excluded, tooYoung, referenced, orphans, refs);
            return;
        }

        string date = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string? refusal = RefusalReason(orphans.Count, referenced, refs.Keys.Count);
        if (refusal is not null)
        {
            logger.LogError(
                "OrphanedObjectSweepJob (quarantine, refused) listed {Listed} object(s): {Excluded} excluded, {TooYoung} younger than {MinimumAgeDays} days, " +
                "{Referenced} referenced, {Orphans} unreferenced ({OrphanBytes} bytes); moved 0 because {Reason}; hard-deleted 0. " +
                "References: {Keys} key(s) from {Values} stored value(s), {OnNoKnownAddress} on no known address; _quarantine/ not listed",
                listed.Count, excluded, tooYoung, (int)MinimumAge.TotalDays,
                referenced, orphans.Count, orphans.Sum(o => o.Object.SizeBytes), refusal,
                refs.Keys.Count, refs.ValuesRead, refs.ValuesOnNoKnownAddress);
            // A brake that engages silently is not a brake: an error line reaches nobody, a Failed job reaches
            // the Hangfire alert. Summary first, so the diagnostic survives the failure.
            throw new InvalidOperationException($"OrphanedObjectSweepJob refused to move anything: {refusal}. See the summary line above.");
        }

        int copyFailed = 0, notVerified = 0, deleteFailed = 0;
        Dictionary<(string Owner, string Area), (int Count, long Bytes)> moved = [];

        foreach ((R2ObjectInfo obj, (string Owner, string Area) group) in orphans)
        {
            string destination = $"{QuarantineFolder}{date}/{obj.Key}";

            try { await r2.CopyAsync(obj.Key, destination, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                copyFailed++;
                LogStepFailure("copy", group, ex, "the original stays in place");
                continue;
            }

            // A copy is only trusted once an independent HEAD reads it back with the original's size AND ETag.
            // CopyObject's 200 is the API's promise; the HEAD is evidence. S3's CopyObject can even fail after
            // its 200, and the original is deleted next, so this is what stands between a bad copy and a lost
            // file. A length alone is not an identity; the ETag is (verified on staging 23 Nov: a server-side
            // copy keeps the ETag). These objects arrive by presigned single PUT, so their ETags are plain MD5.
            // If a multipart upload path is ever added (ETag "<md5>-<n>"), re-check that a copy preserves it
            // before trusting this for those objects. A missing ETag on either side fails the check: the
            // original stays.
            R2ObjectStat? copy;
            try { copy = await r2.HeadAsync(destination, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                notVerified++;
                LogStepFailure("verify", group, ex, "the original stays in place");
                continue;
            }
            string? mismatch = VerifyMismatch(obj, copy);
            if (mismatch is not null)
            {
                notVerified++;
                logger.LogWarning(
                    "OrphanedObjectSweepJob (quarantine) verify failed for one object, owner {Owner}, area {Area}: {Mismatch}; the original stays in place",
                    group.Owner, group.Area, mismatch);
                continue;
            }

            try { await r2.DeleteAsync(obj.Key, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                deleteFailed++;
                LogStepFailure("delete", group, ex, "the original stays in place and a copy is in quarantine");
                continue;
            }

            moved.TryGetValue(group, out (int Count, long Bytes) sum);
            moved[group] = (sum.Count + 1, sum.Bytes + obj.SizeBytes);
        }

        foreach (KeyValuePair<(string Owner, string Area), (int Count, long Bytes)> g in
                 moved.OrderBy(o => o.Key.Owner, StringComparer.Ordinal).ThenBy(o => o.Key.Area, StringComparer.Ordinal))
        {
            logger.LogInformation(
                "OrphanedObjectSweepJob (quarantine) moved {Count} object(s), {Bytes} bytes, owner {Owner}, area {Area} to _quarantine/{QuarantineDate}/",
                g.Value.Count, g.Value.Bytes, g.Key.Owner, g.Key.Area, date);
        }

        int movedCount = moved.Values.Sum(v => v.Count);
        logger.LogInformation(
            "OrphanedObjectSweepJob (quarantine) listed {Listed} object(s): {Excluded} excluded, {TooYoung} younger than {MinimumAgeDays} days, " +
            "{Referenced} referenced, {Orphans} unreferenced ({OrphanBytes} bytes); moved {Moved} ({MovedBytes} bytes) to _quarantine/{QuarantineDate}/, " +
            "{CopyFailed} copy failed, {NotVerified} copy not verified, {DeleteFailed} copied but original kept; hard-deleted 0. " +
            "References: {Keys} key(s) from {Values} stored value(s), {OnNoKnownAddress} on no known address; _quarantine/ not listed",
            listed.Count, excluded, tooYoung, (int)MinimumAge.TotalDays,
            referenced, orphans.Count, orphans.Sum(o => o.Object.SizeBytes), movedCount, moved.Values.Sum(v => v.Bytes), date,
            copyFailed, notVerified, deleteFailed,
            refs.Keys.Count, refs.ValuesRead, refs.ValuesOnNoKnownAddress);

        // Same rule as R2ExportService: one object failing is logged and retried next run, but EVERY object
        // failing is a systemic problem (lost permission, a lock on the wrong prefix), so it fails the job,
        // which HangfireJobOutcomeLogFilter logs and the Hangfire alert can see.
        int failed = copyFailed + notVerified + deleteFailed;
        if (failed > 0 && movedCount == 0)
            throw new InvalidOperationException(
                $"OrphanedObjectSweepJob: all {failed} move(s) failed ({copyFailed} copy, {notVerified} verify, {deleteFailed} delete). See the warnings above.");
    }

    private void ReportOnly(
        int listed, int excluded, int tooYoung, int referenced,
        List<(R2ObjectInfo Object, (string Owner, string Area) Group)> orphans, StoredObjectReferences refs)
    {
        // Phase 2's two lines, plus "_quarantine/ not listed" on the summary (23 Nov): the same suffix as the
        // quarantine line, so "listed N" means one thing in every mode. No prediction is outstanding against
        // the old wording.
        foreach (IGrouping<(string Owner, string Area), (R2ObjectInfo Object, (string Owner, string Area) Group)> g in orphans
                     .GroupBy(o => o.Group)
                     .OrderBy(g => g.Key.Owner, StringComparer.Ordinal).ThenBy(g => g.Key.Area, StringComparer.Ordinal))
        {
            logger.LogInformation(
                "OrphanedObjectSweepJob (report-only) would delete {Count} object(s), {Bytes} bytes, owner {Owner}, area {Area}",
                g.Count(), g.Sum(o => o.Object.SizeBytes), g.Key.Owner, g.Key.Area);
        }

        logger.LogInformation(
            "OrphanedObjectSweepJob (report-only) listed {Listed} object(s): {Excluded} excluded, {TooYoung} younger than {MinimumAgeDays} days, " +
            "{Referenced} referenced, {Orphans} unreferenced ({OrphanBytes} bytes) that it would delete; deleted 0. " +
            "References: {Keys} key(s) from {Values} stored value(s), {OnNoKnownAddress} on no known address; _quarantine/ not listed",
            listed, excluded, tooYoung, (int)MinimumAge.TotalDays,
            referenced, orphans.Count, orphans.Sum(o => o.Object.SizeBytes),
            refs.Keys.Count, refs.ValuesRead, refs.ValuesOnNoKnownAddress);
    }

    /// <summary>
    /// Off unless the setting says exactly "Quarantine" (any case). An unknown value runs report-only and says
    /// so, so a typo can never switch the move on, and never goes unnoticed either.
    /// </summary>
    private bool ResolveQuarantineMode()
    {
        string? mode = options.Value.Mode;
        if (string.IsNullOrWhiteSpace(mode) || string.Equals(mode, "ReportOnly", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(mode, "Quarantine", StringComparison.OrdinalIgnoreCase)) return true;
        logger.LogWarning(
            "OrphanedObjectSweepJob: {Setting} is '{Mode}', which is not a mode (ReportOnly, Quarantine); running report-only",
            OrphanedObjectSweepOptions.Section + ":Mode", mode);
        return false;
    }

    /// <summary>Null when the copy matches the original; otherwise what differs. Never names the key.</summary>
    internal static string? VerifyMismatch(R2ObjectInfo original, R2ObjectStat? copy)
    {
        if (copy is null) return "the copy is missing";
        if (copy.SizeBytes != original.SizeBytes)
            return string.Create(CultureInfo.InvariantCulture, $"the copy is {copy.SizeBytes} bytes against {original.SizeBytes}");
        string? want = NormalizeETag(original.ETag), got = NormalizeETag(copy.ETag);
        if (want is null || got is null) return "an ETag is missing, so the copy can't be identified";
        if (!string.Equals(want, got, StringComparison.Ordinal)) return "the copy's ETag differs from the original's";
        return null;
    }

    /// <summary>S3 sends ETags quoted; compare the value inside the quotes.</summary>
    private static string? NormalizeETag(string? etag)
    {
        string? v = etag?.Trim().Trim('"');
        return string.IsNullOrEmpty(v) ? null : v;
    }

    internal static string? RefusalReason(int unreferenced, int referenced, int referenceKeys)
    {
        if (unreferenced > 0 && referenceKeys == 0)
            return "the reference reader returned no keys at all";
        int candidates = unreferenced + referenced;
        if (candidates >= RefusalMinimumCount && unreferenced > candidates * RefusalShare)
            return string.Create(CultureInfo.InvariantCulture,
                $"{unreferenced} of {candidates} candidates look unreferenced, more than {RefusalShare * 100:0}%");
        return null;
    }

    private void LogStepFailure(string step, (string Owner, string Area) group, Exception ex, string consequence)
    {
        // The error code and status only: an exception message can quote the key, and this job logs no keys.
        string error = ex is AmazonS3Exception s3
            ? string.Create(CultureInfo.InvariantCulture, $"{s3.ErrorCode} ({(int)s3.StatusCode})")
            : ex.GetType().Name;
        logger.LogWarning(
            "OrphanedObjectSweepJob (quarantine) {Step} failed for one object, owner {Owner}, area {Area}: {Error}; {Consequence}",
            step, group.Owner, group.Area, error, consequence);
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
