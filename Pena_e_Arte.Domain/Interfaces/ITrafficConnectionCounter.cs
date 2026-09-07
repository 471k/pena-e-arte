namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// Thread-safe count of currently-connected TrafficHub clients, so TrafficBroadcastService can
/// skip its Redis work entirely when no admin has the live-traffic page open. A DI-registered
/// singleton (not a bare static field) so it stays testable and injectable into both the hub
/// and the background service.
/// </summary>
public interface ITrafficConnectionCounter
{
    int Count { get; }
    void Increment();
    void Decrement();
}
