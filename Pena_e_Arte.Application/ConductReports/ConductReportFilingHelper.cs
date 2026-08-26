using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConductReports;

/// <summary>
/// Shared tail of <c>FileArtistConductReportHandler</c>/<c>FileStudioConductReportHandler</c> —
/// save the report, then fire the High-severity alert. Isolates the Artist/Studio difference to
/// just the caller's own lookup + <see cref="ConductReport"/> factory call.
/// </summary>
internal static class ConductReportFilingHelper
{
    public static async Task FileAsync(
        IAppDbContext db,
        INotificationService notifications,
        ILogger logger,
        Studio studio,
        Func<ConductReport> createReport,
        CancellationToken ct)
    {
        ConductReport report = createReport();

        db.ConductReports.Add(report);
        await db.SaveChangesAsync(ct);

        await ConductReportNotifier.NotifyIfHighSeverityAsync(notifications, studio, report, logger, ct);
    }
}
