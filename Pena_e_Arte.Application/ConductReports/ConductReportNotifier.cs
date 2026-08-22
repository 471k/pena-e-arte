using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConductReports;

/// <summary>
/// Fires the immediate owner+issuer alert email for a High-severity report. Standard-severity
/// reports are a no-op here — they surface purely via the in-app inboxes (owner/artist/issuer
/// queries). Deliberately bypasses NotificationLog/NotificationType/
/// StudioNotificationPreference — same direct INotificationService.SendEmailAsync call shape as
/// SubmitContactRequestHandler — an owner must not be able to opt out of this via notification
/// preferences (a sexual-misconduct or scam report about their own studio/artist is a direct
/// conflict of interest to bury).
///
/// A plain static helper (methods take IAppDbContext/INotificationService as parameters), not a
/// DI-registered service — matches the existing convention for this class of single-purpose
/// Application-layer helper: FeedbackAccessGuard and ConductReportAuthorizationGuard follow the
/// same shape, and neither is registered in Program.cs.
/// </summary>
internal static class ConductReportNotifier
{
    public static async Task NotifyIfHighSeverityAsync(
        IAppDbContext db, INotificationService notifications, ConductReport report, CancellationToken ct)
    {
        if (!ReportCategoryClassifier.IsHighSeverity(report.Category)) return;

        Studio? studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == report.StudioId, ct);
        if (studio is null) return;

        string subject = $"[Urgent] {report.Category} report filed at {studio.Name}";
        string body =
            $"<p>A <strong>{report.Category}</strong> conduct report was just filed" +
            (report.ArtistId is not null ? " against an artist" : "") +
            $" at <strong>{studio.Name}</strong>.</p>" +
            "<p>Review it in the dashboard as soon as possible.</p>";

        await notifications.SendEmailAsync(studio.OwnerEmail, subject, body, ct);
        await notifications.SendEmailAsync(PlatformContacts.SupportEmail, subject, body, ct);
    }
}
