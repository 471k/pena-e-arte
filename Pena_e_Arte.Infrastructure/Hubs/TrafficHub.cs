using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Hubs;

/// <summary>
/// Unlike ScheduleHub/DesignHub/NotificationHub (per-studio groups, any authenticated tenant
/// member), this hub has exactly one group, "platform:traffic", because every client able to
/// connect at all is by definition already issuer-scoped by the [Authorize] policy below — no
/// per-studio partitioning needed, no risk of the P0 cross-tenant SignalR bug fixed 2026-07-26
/// (that fix validated tenant_id against a requested studioId for hubs any authenticated role
/// could join; this hub only issuers can join at all).
/// </summary>
[Authorize(Policy = "IssuerOnly")]
public class TrafficHub(ITrafficConnectionCounter connectionCounter) : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "platform:traffic");
        connectionCounter.Increment();
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        connectionCounter.Decrement();
        await base.OnDisconnectedAsync(exception);
    }
}
