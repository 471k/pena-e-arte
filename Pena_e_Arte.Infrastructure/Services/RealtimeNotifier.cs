using Microsoft.AspNetCore.SignalR;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Hubs;

namespace Pena_e_Arte.Infrastructure.Services;

public class RealtimeNotifier(
    IHubContext<ScheduleHub>     scheduleHub,
    IHubContext<DesignHub>       designHub,
    IHubContext<NotificationHub> notificationHub,
    IHubContext<SupportHub>      supportHub) : IRealtimeNotifier
{
    private static readonly HashSet<string> DesignEvents =
    [
        "DesignUploaded", "DesignReviewed", "DesignRevisionExpired"
    ];

    public async Task NotifyStudioAsync(Guid studioId, string eventName, object payload, CancellationToken ct)
    {
        string group = $"studio:{studioId}";
        IClientProxy target = eventName switch
        {
            "NotificationReceived" => notificationHub.Clients.Group(group),
            _ when DesignEvents.Contains(eventName) => designHub.Clients.Group(group),
            _ => scheduleHub.Clients.Group(group)
        };
        await target.SendAsync(eventName, payload, ct);
    }

    public async Task NotifyTicketAsync(Guid feedbackReportId, string eventName, object payload, CancellationToken ct) =>
        await supportHub.Clients.Group($"ticket:{feedbackReportId}").SendAsync(eventName, payload, ct);
}
