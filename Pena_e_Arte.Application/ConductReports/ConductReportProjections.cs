using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.ConductReports;

/// <summary>
/// Shared join + response mapping for every ConductReport read (owner, artist, admin). The
/// entity itself stores only StudioId/ArtistId/AppointmentId, so every caller needs the same
/// join against Studios/Artists/Appointments to render StudioName/ArtistName/AppointmentDate —
/// centralized here so a future caller can't forget a join and ship a response missing display
/// fields.
///
/// The Studios/Appointments joins need no IgnoreQueryFilters() (neither entity carries a
/// tenant filter that would block them here — Studio has none; Appointment's is bypassed
/// deliberately below). The Artists join DOES need IgnoreQueryFilters(): Artist is a
/// TenantEntity, and the admin caller (GetConductReportsHandler) has no tenant set at all, so
/// without it every artist-target report issued from any studio other than none would resolve
/// ArtistName to null. This is safe for the owner/artist callers too, since in both cases the
/// outer ConductReports query is already scoped to the caller's own StudioId/ArtistId before
/// this join ever runs — IgnoreQueryFilters() here only ever widens the *lookup*, never which
/// ConductReport rows are visible. Same reasoning as the Appointments join (needed for
/// AppointmentDate — a report's target appointment isn't necessarily in the caller's tenant
/// context either, e.g. an admin with no tenant).
/// </summary>
internal static class ConductReportProjections
{
    private sealed record Joined(
        Guid Id,
        Guid StudioId,
        string StudioName,
        Guid? ArtistId,
        string? ArtistFirstName,
        string? ArtistLastName,
        Guid AppointmentId,
        DateTime AppointmentDate,
        ReportCategory Category,
        string Reason,
        List<string> AttachmentUrls,
        ReportStatus Status,
        string? ResolutionNote,
        DateTime? ResolvedAt,
        DateTime CreatedAt,
        Guid ReporterUserId,
        string ReporterName);

    private static IQueryable<Joined> Join(IQueryable<ConductReport> reports, IAppDbContext db) =>
        from r in reports
        join s in db.Studios on r.StudioId equals s.Id
        join a in db.Artists.IgnoreQueryFilters() on r.ArtistId equals (Guid?)a.Id into artistJoin
        from a in artistJoin.DefaultIfEmpty()
        join ap in db.Appointments.IgnoreQueryFilters() on r.AppointmentId equals ap.Id into apptJoin
        from ap in apptJoin.DefaultIfEmpty()
        select new Joined(
            r.Id,
            r.StudioId,
            s.Name,
            r.ArtistId,
            a == null ? null : a.FirstName,
            a == null ? null : a.LastName,
            r.AppointmentId,
            ap == null ? default : ap.Date,
            r.Category,
            r.Reason,
            r.AttachmentUrls,
            r.Status,
            r.ResolutionNote,
            r.ResolvedAt,
            r.CreatedAt,
            r.ReporterUserId,
            r.ReporterName);

    private static ConductReportResponse Map(Joined j, bool redact) => new(
        j.Id,
        j.StudioId,
        j.StudioName,
        j.ArtistId,
        j.ArtistFirstName is null ? null : $"{j.ArtistFirstName} {j.ArtistLastName}".Trim(),
        j.AppointmentId,
        j.AppointmentDate,
        j.Category.ToString(),
        ReportCategoryClassifier.IsHighSeverity(j.Category),
        j.Reason,
        j.AttachmentUrls,
        j.Status.ToString(),
        j.ResolutionNote,
        j.ResolvedAt,
        j.CreatedAt,
        redact ? null : j.ReporterUserId,
        redact ? null : j.ReporterName);

    /// <summary>Owner/admin callers — full reporter identity included.</summary>
    public static async Task<List<ConductReportResponse>> ToFullResponseAsync(
        IQueryable<ConductReport> reports, IAppDbContext db, CancellationToken ct)
    {
        List<Joined> joined = await Join(reports, db).ToListAsync(ct);
        return joined.Select(j => Map(j, redact: false)).ToList();
    }

    /// <summary>Artist caller — reporter identity always redacted, regardless of what the
    /// underlying row carries. Never reuse ToFullResponseAsync for this caller.</summary>
    public static async Task<List<ConductReportResponse>> ToRedactedResponseAsync(
        IQueryable<ConductReport> reports, IAppDbContext db, CancellationToken ct)
    {
        List<Joined> joined = await Join(reports, db).ToListAsync(ct);
        return joined.Select(j => Map(j, redact: true)).ToList();
    }
}
