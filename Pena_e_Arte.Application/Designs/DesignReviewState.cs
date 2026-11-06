using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Designs;

/// <summary>Whose move it is on a design (E04c, A05).</summary>
public enum DesignWaitingOn
{
    /// <summary>Nothing is outstanding: the latest version is approved, or it is a catalog template.</summary>
    Nobody,

    /// <summary>The artist owes a version: none uploaded yet, or the client asked for changes to the
    /// latest one, or its review window expired without an answer.</summary>
    Artist,

    /// <summary>The latest version is with the client and they haven't answered yet.</summary>
    Client,
}

/// <summary>A design's derived state: its status, whose move it is, and the version it is about.</summary>
public readonly record struct DesignProgress(string Status, DesignWaitingOn WaitingOn, DesignRevision? Latest);

/// <summary>
/// The one home for a design's derived state (E04c, A05). A design has no status column; everything
/// here comes from its revisions and their approvals, and only the LATEST revision (highest version
/// number) decides. A review of an older revision is history and moves nothing.
///
/// Status keeps the strings GetDesignsHandler has always returned (Draft, InReview, Approved,
/// ChangesRequested; an Expired approval counts as ChangesRequested because the artist is next either
/// way). WaitingOn separates work waiting on the artist from work waiting on the client, which Status
/// alone did not: "Draft" and "ChangesRequested" are both the artist's queue, "InReview" is the
/// client's.
///
/// A catalog template has no client to review it, so it waits on nobody whatever its revisions say.
/// </summary>
public static class DesignReviewState
{
    /// <summary>
    /// The design's latest live revision (highest version number), or null with none: the one a client
    /// may review (ReviewDesignHandler enforces latest-only since 2026-11-06) and the one asking for an
    /// earlier version back is recorded against.
    /// </summary>
    public static Task<DesignRevision?> LatestRevisionAsync(IAppDbContext db, Guid designId, CancellationToken ct) =>
        db.DesignRevisions
            .Include(r => r.Approval)
            .Where(r => r.DesignId == designId && r.DeletedAt == null)
            .OrderByDescending(r => r.VersionNumber)
            .FirstOrDefaultAsync(ct);

    /// <summary>The number the next revision gets: one above the highest live version (never a count, which
    /// repeats a number once a middle version has been deleted).</summary>
    public static async Task<int> NextVersionNumberAsync(IAppDbContext db, Guid designId, CancellationToken ct) =>
        (await db.DesignRevisions.Where(r => r.DesignId == designId && r.DeletedAt == null).MaxAsync(r => (int?)r.VersionNumber, ct) ?? 0) + 1;

    public static DesignProgress Derive(Design design)
    {
        DesignRevision? latest = design.Revisions
            .Where(r => r.DeletedAt == null)
            .OrderByDescending(r => r.VersionNumber)
            .FirstOrDefault();

        if (latest is null)
            return new DesignProgress("Draft", design.IsCatalogItem ? DesignWaitingOn.Nobody : DesignWaitingOn.Artist, null);

        (string status, DesignWaitingOn waitingOn) = latest.Approval?.Status switch
        {
            null => ("InReview", DesignWaitingOn.Client),
            DesignApprovalStatus.Pending => ("InReview", DesignWaitingOn.Client),
            DesignApprovalStatus.Approved => ("Approved", DesignWaitingOn.Nobody),
            DesignApprovalStatus.ChangesRequested => ("ChangesRequested", DesignWaitingOn.Artist),
            DesignApprovalStatus.Expired => ("ChangesRequested", DesignWaitingOn.Artist),
            _ => ("InReview", DesignWaitingOn.Client),
        };

        return new DesignProgress(status, design.IsCatalogItem ? DesignWaitingOn.Nobody : waitingOn, latest);
    }
}
