using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConductReports;

/// <summary>
/// Fires the immediate owner+admin alert email for a High-severity report. Standard-severity
/// reports are a no-op here — they surface purely via the in-app inboxes (owner/artist/admin
/// queries). Deliberately bypasses NotificationLog/NotificationType/
/// StudioNotificationPreference — same direct INotificationService.SendEmailAsync call shape as
/// SubmitContactRequestHandler — an owner must not be able to opt out of this via notification
/// preferences (a sexual-misconduct or scam report about their own studio/artist is a direct
/// conflict of interest to bury).
///
/// A plain static helper (methods take dependencies as parameters), not a DI-registered service
/// — matches the existing convention for this class of single-purpose Application-layer helper:
/// FeedbackAccessGuard and ConductReportAuthorizationGuard follow the same shape, and neither is
/// registered in Program.cs.
///
/// Takes the already-loaded <see cref="Studio"/> rather than re-querying it — the caller
/// (<see cref="ConductReportFilingHelper"/>) always has it in hand already (the studio-target
/// handler resolved it as the filing target itself; the artist-target handler resolves it once
/// via the artist's StudioId), so a second query here would be pure waste.
/// </summary>
internal static class ConductReportNotifier
{
    public static async Task NotifyIfHighSeverityAsync(
        INotificationService notifications, Studio studio, ConductReport report, ILogger logger, CancellationToken ct)
    {
        if (!ReportCategoryClassifier.IsHighSeverity(report.Category)) return;

        string subject = $"[Urgent] {report.Category} report filed at {studio.Name}";
        string body =
            $"<p>A <strong>{report.Category}</strong> conduct report was just filed" +
            (report.ArtistId is not null ? " against an artist" : "") +
            $" at <strong>{studio.Name}</strong>.</p>" +
            "<p>Review it in the dashboard as soon as possible.</p>";

        // Filing a report must never fail because the alert email failed — the ConductReport
        // row is already committed by the time this runs. Each send is wrapped individually
        // (not just the Task.WhenAll) so a failure on one recipient doesn't cancel the other.
        await Task.WhenAll(
            SendSafelyAsync(notifications, logger, report, studio.OwnerEmail, subject, body, ct),
            SendSafelyAsync(notifications, logger, report, PlatformContacts.SupportEmail, subject, body, ct));
    }

    private static async Task SendSafelyAsync(
        INotificationService notifications, ILogger logger, ConductReport report,
        string to, string subject, string body, CancellationToken ct)
    {
        try
        {
            await notifications.SendEmailAsync(to, subject, body, ct);
        }
        catch (Exception ex)
        {
            // No PII: report id, category, and studio id only — never the recipient address,
            // reason text, or reporter identity.
            logger.LogError(ex,
                "Failed to send High-severity conduct-report alert email {@ReportId} {@Category} {@StudioId}",
                report.Id, report.Category, report.StudioId);
        }
    }
}
