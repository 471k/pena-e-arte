namespace Pena_e_Arte.Domain.Interfaces;

public interface IRealtimeNotifier
{
    Task NotifyStudioAsync(Guid studioId, string eventName, object payload, CancellationToken ct = default);
    Task NotifyTicketAsync(Guid feedbackReportId, string eventName, object payload, CancellationToken ct = default);
    Task NotifyUserAsync(Guid userId, string eventName, object payload, CancellationToken ct = default);
}
