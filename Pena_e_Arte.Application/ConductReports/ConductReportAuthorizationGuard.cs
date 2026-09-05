using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ConductReports;

/// <summary>
/// Shared by every handler that loads a single ConductReport by id — centralizes both read
/// authorization (ConductReport.IsReadableBy) and the severity-gated write rule, the same
/// reasoning FeedbackAccessGuard gives for FeedbackReport: one place, not N copies that can
/// drift.
/// </summary>
internal static class ConductReportAuthorizationGuard
{
    public static async Task<ConductReport> LoadReadableReportAsync(
        IAppDbContext db, Guid reportId, ICurrentUser user, ICurrentTenant tenant, CancellationToken ct)
    {
        ConductReport report = await db.ConductReports.FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw new NotFoundException(nameof(ConductReport), reportId);

        Guid? callerArtistId = null;
        if (string.Equals(user.Role, "artist", StringComparison.OrdinalIgnoreCase))
        {
            Artist? me = await db.Artists.FirstOrDefaultAsync(a => a.UserId == user.UserId, ct);
            callerArtistId = me?.Id;
        }

        Guid? callerStudioId = tenant.IsSet ? tenant.StudioId : null;

        if (!report.IsReadableBy(callerStudioId, callerArtistId, user.Role))
            throw new ForbiddenException("You do not have access to this report.");

        return report;
    }

    /// <summary>Owner may change status only for Standard-severity reports about their own
    /// studio; issuer may always change status; artist may never change status. A
    /// sexual-misconduct or scam report about the owner's own artist is a direct conflict of
    /// interest for the owner to resolve/dismiss — same reasoning class as why
    /// IgnoreQueryFilters() is restricted to IssuerOnly handlers by default.</summary>
    public static void EnsureCanChangeStatus(ConductReport report, ICurrentUser user)
    {
        if (string.Equals(user.Role, "issuer", StringComparison.OrdinalIgnoreCase)) return;

        if (string.Equals(user.Role, "owner", StringComparison.OrdinalIgnoreCase)
            && !ReportCategoryClassifier.IsHighSeverity(report.Category))
            return;

        throw new ForbiddenException(
            "High-severity reports can only be resolved by platform staff.");
    }
}
