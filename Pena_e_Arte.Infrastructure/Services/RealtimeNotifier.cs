using Microsoft.AspNetCore.SignalR;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Hubs;

namespace Pena_e_Arte.Infrastructure.Services;

public class RealtimeNotifier(IHubContext<ScheduleHub> hub) : IRealtimeNotifier
{
    public async Task NotifyStudioAsync(Guid studioId, string eventName, object payload, CancellationToken ct) =>
        await hub.Clients
            .Group($"studio:{studioId}")
            .SendAsync(eventName, payload, ct);
}
