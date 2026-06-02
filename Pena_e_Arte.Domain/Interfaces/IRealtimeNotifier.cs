namespace Pena_e_Arte.Domain.Interfaces;

public interface IRealtimeNotifier
{
    Task NotifyStudioAsync(Guid studioId, string eventName, object payload, CancellationToken ct = default);
}
