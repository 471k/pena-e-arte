using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Feedback;

/// <summary>
/// Shared by every handler that loads a single FeedbackReport by id and must enforce
/// FeedbackReport.IsAccessibleBy — centralized so a future handler on this resource can't
/// forget the check the way two independent copies of the same four lines could drift.
/// </summary>
internal static class FeedbackAccessGuard
{
    public static async Task<FeedbackReport> LoadAccessibleReportAsync(
        IAppDbContext db, Guid reportId, ICurrentUser user, ICurrentTenant tenant, CancellationToken ct)
    {
        FeedbackReport report = await db.FeedbackReports
            .FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw new NotFoundException(nameof(FeedbackReport), reportId);

        if (!report.IsAccessibleBy(user.UserId, tenant.StudioId, user.Role))
            throw new ForbiddenException("You do not have access to this feedback ticket.");

        return report;
    }
}
