using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Pena_e_Arte.Infrastructure.Hubs;

// Unlike ScheduleHub/SupportHub's join-a-resource-group-by-id model, every connection here
// auto-joins a personal `user:{userId}` group on connect. A 1:1 conversation only ever has
// two participants, both already fully authenticated on their own connection, so there is
// no resource id a client could leak or guess — this sidesteps the ownership-check bug class
// SupportHub's JoinTicket originally had (see architecture.md's Support Escalation
// code-review entry) by construction, not by an extra check. It also means one connection
// receives MessageReceived for every conversation the user is part of, which the inbox
// unread badge needs regardless of which (if any) thread is currently open.
// /hubs paths are exempt from TenantMiddleware, so ICurrentUser/ICurrentTenant are never
// populated for hub invocations — claims are read directly from Context.User, matching
// every other hub in this codebase.
[Authorize]
public class ChatHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out Guid userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        }
        await base.OnConnectedAsync();
    }
}
