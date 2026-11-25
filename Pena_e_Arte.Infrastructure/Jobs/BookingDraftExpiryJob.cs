using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Deletes booking drafts whose seven days have run out (Phi's decision), with their photo references. Reads
/// already treat an expired draft as none; this removes the row so it is not kept longer than promised. The photos
/// themselves are left to OrphanedObjectSweepJob, which sees them as unreferenced once these rows are gone.
/// </summary>
public class BookingDraftExpiryJob(IAppDbContext db, ILogger<BookingDraftExpiryJob> logger, TimeProvider time)
{
    public async Task<string> RunAsync(CancellationToken ct = default)
    {
        DateTime now = time.GetUtcNow().UtcDateTime;
        // IgnoreQueryFilters approved: usage #56, booking-draft expiry, a cross-tenant Hangfire job with no tenant set.
        // See architecture.md.
        List<BookingDraft> expired = await db.BookingDrafts
            .IgnoreQueryFilters()
            .Include(d => d.Attachments)
            .Where(d => d.ExpiresAt <= now)
            .ToListAsync(ct);

        foreach (BookingDraft draft in expired)
        {
            db.BookingDraftAttachments.RemoveRange(draft.Attachments);
            db.BookingDrafts.Remove(draft);
        }
        await db.SaveChangesAsync(ct);

        logger.LogInformation("BookingDraftExpiryJob deleted {Count} expired draft(s)", expired.Count);
        return $"deleted {expired.Count} expired draft(s)";
    }
}
