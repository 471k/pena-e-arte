using StackExchange.Redis;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// With abortConnect=false, ConnectionMultiplexer.ConnectAsync returns after the FIRST connect attempt
/// even when it failed. At pod start that attempt can fail (the pod's network isn't ready for the first
/// second or two), and SignalR's Redis backplane then subscribed to its channels on a disconnected
/// multiplexer — logging "Not connected to Redis." 3-5 times per new API pod on every deploy (207 lines
/// in 7 days, all within ~2-4 s of a pod starting) and risking a pod that misses the other replica's
/// hub messages. Waiting briefly for the connection before subscribing removes the race; if Redis
/// really is down, the caller carries on after <paramref name="max"/> exactly as before.
///
/// The wait is a DURATION, so it is measured on the monotonic clock (GetTimestamp/GetElapsedTime), never the
/// wall clock. It used to compute a deadline as DateTime.UtcNow + max: a wall-clock step (NTP, a VM resume)
/// stretches or cuts that deadline, and a clock stepped back would hold pod start for as long as the step.
/// TimeProvider.GetUtcNow() would not fix that; it is the same wall clock behind an abstraction. See
/// backend.md, "Clocks", for the two clock defect classes.
/// </summary>
public static class RedisConnectionWait
{
    public static async Task WaitUntilConnectedAsync(
        IConnectionMultiplexer connection, TimeSpan max, TimeSpan pollInterval, CancellationToken ct = default,
        TimeProvider? timeProvider = null)
    {
        TimeProvider clock = timeProvider ?? TimeProvider.System;
        long started = clock.GetTimestamp();
        while (!connection.IsConnected && clock.GetElapsedTime(started) < max)
            await Task.Delay(pollInterval, clock, ct);
    }
}
