using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Pena_e_Arte.Infrastructure.Hubs;

// JoinTicket/LeaveTicket do not validate ticket ownership before adding the caller to the
// group — matches ScheduleHub.JoinStudio's existing precedent (no membership check there
// either). Practical exposure is bounded by the ticket id being an unguessable Guid, same
// reasoning as ScheduleHub's studioId groups. See architecture.md.
[Authorize]
public class SupportHub : Hub
{
    public async Task JoinTicket(string feedbackReportId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"ticket:{feedbackReportId}");

    public async Task LeaveTicket(string feedbackReportId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ticket:{feedbackReportId}");
}
