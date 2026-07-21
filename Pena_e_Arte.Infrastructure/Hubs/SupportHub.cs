using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Hubs;

// Unlike ScheduleHub.JoinStudio (studio-wide operational data any studio member already
// sees), a support ticket is a private two-party conversation, so JoinTicket validates
// ownership before adding the caller to the group — a leaked/guessed ticket id must not
// grant access to message content. /hubs paths are exempt from TenantMiddleware (see
// TenantMiddleware.ExemptPrefixes), so ICurrentUser/ICurrentTenant are never populated for
// hub invocations; claims are read directly from Context.User instead, matching
// TenantMiddleware's and CurrentUserService's own extraction logic exactly.
[Authorize]
public class SupportHub(IAppDbContext db) : Hub
{
    public async Task JoinTicket(string feedbackReportId)
    {
        if (!Guid.TryParse(feedbackReportId, out Guid reportId)) return;

        Guid userId = Guid.TryParse(
            Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out Guid uid) ? uid : Guid.Empty;
        string role = Context.User?.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        Guid studioId = Guid.TryParse(
            Context.User?.FindFirstValue("tenant_id"), out Guid sid) ? sid : Guid.Empty;

        FeedbackReport? report = await db.FeedbackReports.FirstOrDefaultAsync(r => r.Id == reportId);
        if (report is null || !report.IsAccessibleBy(userId, studioId, role)) return;

        await Groups.AddToGroupAsync(Context.ConnectionId, $"ticket:{feedbackReportId}");
    }

    public async Task LeaveTicket(string feedbackReportId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ticket:{feedbackReportId}");
}
