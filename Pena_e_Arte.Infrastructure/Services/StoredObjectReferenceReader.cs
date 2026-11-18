using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>Every stored-object key some row points at, and how the stored values were read.</summary>
/// <param name="Keys">The referenced keys. A key is protected when it is in this set.</param>
/// <param name="ValuesRead">Non-empty stored values read across all sources.</param>
/// <param name="ValuesOnNoKnownAddress">Values that matched none of the bucket's known addresses: external
/// links (portfolio accepts any URL) or an address the bucket was served from that nobody recorded. They
/// still protect the key their URL path names; the count is reported so an unknown address shows up.</param>
public sealed record StoredObjectReferences(
    IReadOnlySet<string> Keys, int ValuesRead, int ValuesOnNoKnownAddress)
{
    public bool IsReferenced(string objectKey) => Keys.Contains(objectKey);
}

/// <summary>
/// The one place that answers "is this stored object referenced by anything?" for the orphaned-object
/// sweep (docs/claude/overnight-prompt-orphaned-object-cleanup-2026-11-15.md, Phase 1).
///
/// Its sources are exactly the 11 columns of the Phase 0 table (claude-shared
/// code-report-orphaned-object-phase0-2026-11-15.md §1), found as a superset over every string column of
/// the model minus the ones that cannot hold a reference. A new column that stores an object's address
/// MUST be added here, or the sweep will treat its objects as orphans; StoredObjectReferenceReaderTests
/// has one test per source.
///
/// Rules, each one a way the sweep could otherwise delete a live file:
/// - **Every key is checked against every column.** IsR2Url is a host check only, so any validated URL
///   column can hold any object in any studio (Phase 0 §3.2); the prefix never says which column to ask.
/// - **Soft-deleted rows count.** A soft-deleted row still points at its object and can be restored, so
///   every tenant-filtered read below is IgnoreQueryFilters() WITHOUT a DeletedAt predicate — the
///   deliberate exception to the 14 Nov rule. GuestPendingUploadCleanupJob makes the same choice.
/// - **Cross-tenant, no tenant set.** The sweep runs as a Hangfire job; the tenant filter would match
///   nothing (StudioId == Guid.Empty) and every object would look unreferenced.
/// - **Matched on the key, never on one address form.** A value yields the key behind any known address
///   AND the path after its host, so a URL on an address nobody recorded still protects its object.
///   Extra candidates can only protect more, never less.
/// - **A source that cannot be read throws.** Nothing here catches; a failed read must abort the run,
///   never read as "nothing references it".
/// </summary>
public class StoredObjectReferenceReader(IAppDbContext db, IR2Service r2)
{
    public async Task<StoredObjectReferences> ReadAsync(CancellationToken ct)
    {
        List<string> values = [];

        // 1. PortfolioImage.ImageUrl — tenant-filtered; soft-deleted rows protect their objects.
        values.AddRange(await db.PortfolioImages.IgnoreQueryFilters()
            .Select(p => p.ImageUrl).ToListAsync(ct));

        // 2. TattooRecord.PhotoUrls — JSON list; soft-deleted records (DeleteTattooRecordCommand) keep theirs.
        foreach (List<string> urls in await db.TattooRecords.IgnoreQueryFilters()
                     .Select(t => t.PhotoUrls).ToListAsync(ct))
            values.AddRange(urls);

        // 3. DesignRevision.FileUrl — also the source of the catalog copy (RequestCatalogDesignCommand).
        values.AddRange(await db.DesignRevisions.IgnoreQueryFilters()
            .Select(d => d.FileUrl).ToListAsync(ct));

        // 4. AppointmentAttachment.ImageUrl — every pending/ prefix, guest-pending/, and copied designs/.
        values.AddRange(await db.AppointmentAttachments.IgnoreQueryFilters()
            .Select(a => a.ImageUrl).ToListAsync(ct));

        // Sources 5, 6 and 11 take NO IgnoreQueryFilters() because ConductReport, FeedbackReport and Studio have
        // no query filter at all (AppDbContext: "No HasQueryFilter" on the first two; Studio has no entity
        // block). That absence is load-bearing: if a tenant filter is ever added to one of them, this read
        // returns nothing under a job (StudioId == Guid.Empty) and every object it references looks orphaned.
        // Add the bypass here in the same change.

        // 5. ConductReport.AttachmentUrls — JSON list; no tenant filter on this entity.
        foreach (List<string> urls in await db.ConductReports
                     .Select(c => c.AttachmentUrls).ToListAsync(ct))
            values.AddRange(urls);

        // 6. FeedbackReport.AttachmentUrls — JSON list; no tenant filter on this entity.
        foreach (List<string> urls in await db.FeedbackReports
                     .Select(f => f.AttachmentUrls).ToListAsync(ct))
            values.AddRange(urls);

        // 7. IntakeForm.FileUrl — free-text box validated by IsR2Url, so it can name any object.
        values.AddRange((await db.IntakeForms.IgnoreQueryFilters()
            .Where(i => i.FileUrl != null).Select(i => i.FileUrl).ToListAsync(ct))!);

        // 8. ConsentForm.FileUrl — consent/ is excluded from the sweep (RetentionPurgeJob owns it), but
        //    the reader still answers for it, so a misconfigured exclusion cannot delete a consent PDF.
        values.AddRange((await db.ConsentForms.IgnoreQueryFilters()
            .Where(c => c.FileUrl != null).Select(c => c.FileUrl).ToListAsync(ct))!);

        // 9-10. Artist.ProfileImageUrl / AvatarUrl — never written today (Phase 0 rows 9-10); read anyway,
        //    so the first writer is covered without anyone remembering this file.
        var artistImages = await db.Artists.IgnoreQueryFilters()
            .Where(a => a.ProfileImageUrl != null || a.AvatarUrl != null)
            .Select(a => new { a.ProfileImageUrl, a.AvatarUrl }).ToListAsync(ct);
        foreach (var a in artistImages)
        {
            if (a.ProfileImageUrl is not null) values.Add(a.ProfileImageUrl);
            if (a.AvatarUrl is not null) values.Add(a.AvatarUrl);
        }

        // 11. Studio.CoverImageUrl — never written today; Studio has no tenant filter.
        values.AddRange((await db.Studios
            .Where(s => s.CoverImageUrl != null).Select(s => s.CoverImageUrl).ToListAsync(ct))!);

        HashSet<string> keys = new(StringComparer.Ordinal);
        int read = 0;
        int onNoKnownAddress = 0;
        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            read++;
            if (!r2.IsR2Url(value)) onNoKnownAddress++;
            foreach (string key in CandidateKeys(value, r2)) keys.Add(key);
        }

        return new StoredObjectReferences(keys, read, onNoKnownAddress);
    }

    /// <summary>
    /// Every key a stored value might name: the key behind a known address, the path after the host of
    /// any absolute http(s) URL, and the value itself when it is not a URL at all (a bare key).
    ///
    /// The path-after-host candidate is load-bearing, not just generous. TryGetObjectKey returns everything
    /// after the address, query string included, so "https://media.tattooos.co/{studioId}/portfolio/x.jpg?v=2"
    /// yields "{studioId}/portfolio/x.jpg?v=2", which matches no key. The path candidate stops at '?' and '#'
    /// and yields "{studioId}/portfolio/x.jpg", which is what protects that object. Do not remove it.
    ///
    /// Not decoded (accepted residual risk, Desktop 18 Nov): a leading-slash relative path ("/{studioId}/...")
    /// or a protocol-relative URL ("//host/{studioId}/..."). Unreachable today, because nine of the eleven
    /// columns are validated by IsR2Url (a known address) and PortfolioImage holds either a presigned upload
    /// URL or an external link. CandidateKeys_DecodableShapes pins this; relaxing a validator must revisit it.
    /// </summary>
    internal static IEnumerable<string> CandidateKeys(string value, IR2Service r2)
    {
        string trimmed = value.Trim();

        string? known = r2.TryGetObjectKey(trimmed);
        if (!string.IsNullOrEmpty(known)) yield return known;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            // The raw text after "scheme://host[:port]/", up to any query or fragment: keys are stored
            // unescaped (R2Service.GetPublicUrl concatenates), so Uri.AbsolutePath's escaping is avoided.
            int hostStart = trimmed.IndexOf("://", StringComparison.Ordinal) + 3;
            int pathStart = trimmed.IndexOf('/', hostStart);
            if (pathStart >= 0)
            {
                string path = trimmed[(pathStart + 1)..];
                int end = path.IndexOfAny(['?', '#']);
                if (end >= 0) path = path[..end];
                if (path.Length > 0) yield return path;
            }
        }
        else
        {
            yield return trimmed;
        }
    }
}
