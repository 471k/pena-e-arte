using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Hubs;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Ticks every 5s (matches Google Analytics Realtime / Plausible Live's own cadence — see
/// architecture.md's industry-benchmark note). Only does Redis/DB work when at least one issuer
/// has TrafficHub open (ITrafficConnectionCounter), so an idle platform costs nothing. Reads via
/// the same ITrafficPresenceReader the on-demand GetLiveTrafficSnapshotQuery uses, so the two
/// can never drift out of sync with each other.
/// </summary>
public class TrafficBroadcastService(
    IServiceScopeFactory scopeFactory,
    IHubContext<TrafficHub> hubContext,
    ITrafficConnectionCounter connectionCounter,
    ILogger<TrafficBroadcastService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            // WaitForNextTickAsync itself throws OperationCanceledException on shutdown (it
            // doesn't just return false) — that await needs the same cancellation-is-normal
            // handling as the per-tick body below, or a graceful host stop surfaces as
            // BackgroundServiceExceptionBehavior.StopHost's "unhandled exception" fatal log
            // instead of a clean shutdown.
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (connectionCounter.Count <= 0) continue;

                try
                {
                    using IServiceScope scope = scopeFactory.CreateScope();
                    ITrafficPresenceReader reader = scope.ServiceProvider.GetRequiredService<ITrafficPresenceReader>();
                    TrafficPresenceSnapshot snapshot = await reader.ReadSnapshotAsync(stoppingToken);
                    LiveTrafficSnapshotResponse response = snapshot.ToResponse();

                    await hubContext.Clients.Group("platform:traffic")
                        .SendAsync("TrafficSnapshotUpdated", response, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Traffic broadcast tick failed — will retry on the next tick");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on host shutdown — not an error.
        }
    }
}
