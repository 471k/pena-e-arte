using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A client-filed report of misconduct against an artist or a studio. Deliberately NOT a
/// TenantEntity — same non-tenant shape as Review/FeedbackReport/AuditLogEntry: the entity's
/// relevant studio is the *target's* studio, unrelated to the filing client's own current
/// ICurrentTenant.StudioId (see architecture.md Decisions Log, "Client Conduct Reports —
/// 2026-08-22", for why). No EF Core global query filter is registered for this entity — see
/// database.md's IgnoreQueryFilters() table note, which explicitly says no new row was needed
/// here.
/// </summary>
public class ConductReport
{
    private ConductReport() { }

    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>The reported studio. Always set — for an artist-target report this is the
    /// artist's own studio; for a studio-target report it's the studio itself.</summary>
    public Guid StudioId { get; private set; }

    /// <summary>Null when the report targets the studio generally rather than one artist.</summary>
    public Guid? ArtistId { get; private set; }

    /// <summary>The real appointment that establishes the reporter's relationship to the
    /// target. Required for both target kinds — deliberately NOT restricted to
    /// AppointmentStatus.Completed the way Review's eligibility effectively is (a studio must
    /// not be able to dodge every report by simply never marking the appointment complete).</summary>
    public Guid AppointmentId { get; private set; }

    /// <summary>Never exposed in a response the reported artist can read — see
    /// ConductReportProjections and ConductReportResponse mapping. Owner/admin always see it.</summary>
    public Guid ReporterUserId { get; private set; }
    public string ReporterName { get; private set; } = string.Empty;

    public ReportCategory Category { get; private set; }
    public string Reason { get; private set; } = string.Empty;

    /// <summary>Optional evidence — screenshots/short clips, same R2 presign flow and cap (3)
    /// as FeedbackReport.AttachmentUrls.</summary>
    public List<string> AttachmentUrls { get; private set; } = [];

    public ReportStatus Status { get; private set; } = ReportStatus.Open;
    public string? ResolutionNote { get; private set; }
    public DateTime? ResolvedAt { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public static ConductReport ForArtist(
        Guid studioId,
        Guid artistId,
        Guid appointmentId,
        Guid reporterUserId,
        string reporterName,
        ReportCategory category,
        string reason,
        IReadOnlyList<string>? attachmentUrls = null) =>
        new()
        {
            StudioId = studioId,
            ArtistId = artistId,
            AppointmentId = appointmentId,
            ReporterUserId = reporterUserId,
            ReporterName = reporterName.Trim(),
            Category = category,
            Reason = reason.Trim(),
            AttachmentUrls = attachmentUrls?.ToList() ?? [],
        };

    public static ConductReport ForStudio(
        Guid studioId,
        Guid appointmentId,
        Guid reporterUserId,
        string reporterName,
        ReportCategory category,
        string reason,
        IReadOnlyList<string>? attachmentUrls = null) =>
        new()
        {
            StudioId = studioId,
            ArtistId = null,
            AppointmentId = appointmentId,
            ReporterUserId = reporterUserId,
            ReporterName = reporterName.Trim(),
            Category = category,
            Reason = reason.Trim(),
            AttachmentUrls = attachmentUrls?.ToList() ?? [],
        };

    public void UpdateStatus(ReportStatus status, string? resolutionNote)
    {
        Status = status;
        ResolutionNote = resolutionNote?.Trim();
        ResolvedAt = status is ReportStatus.Resolved or ReportStatus.Dismissed
            ? DateTime.UtcNow
            : null;
    }

    /// <summary>Admin: always. Owner: any report targeting their own studio (StudioId match),
    /// regardless of severity — owners can always VIEW a high-severity report about their own
    /// artist, they just can't change its status (enforced separately, see
    /// ConductReportAuthorizationGuard.EnsureCanChangeStatus). Artist: only reports where
    /// ArtistId matches their own — never studio-target reports with no ArtistId.</summary>
    public bool IsReadableBy(Guid? callerStudioId, Guid? callerArtistId, string role)
    {
        if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(role, "owner", StringComparison.OrdinalIgnoreCase))
            return callerStudioId is not null && StudioId == callerStudioId;
        if (string.Equals(role, "artist", StringComparison.OrdinalIgnoreCase))
            return callerArtistId is not null && ArtistId == callerArtistId;
        return false;
    }
}
