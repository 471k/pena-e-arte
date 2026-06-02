using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Pena_e_Arte.Infrastructure.Hubs;

[Authorize]
public class ScheduleHub : Hub
{
    public async Task JoinStudio(string studioId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"studio:{studioId}");

    public async Task LeaveStudio(string studioId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"studio:{studioId}");
}
