namespace Pena_e_Arte.Domain.Interfaces;

public interface IRealtimeNotifier
{
    Task NotifyStudioAsync(Guid studioId, string eventName, object payload, CancellationToken ct = default);
    Task NotifyTicketAsync(Guid feedbackReportId, string eventName, object payload, CancellationToken ct = default);
    Task NotifyUserAsync(Guid userId, string eventName, object payload, CancellationToken ct = default);

    /// <summary>
    /// Broadcasts to every connected admin (platform-wide, not per-studio) — see
    /// NotificationHub's conditional auto-join into "platform:admin-notifications".
    /// </summary>
    Task NotifyAdminsAsync(string eventName, object payload, CancellationToken ct = default);
}
