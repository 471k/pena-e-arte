using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// Single mapping point from the Domain-level presence snapshot to the wire response, shared by
/// GetLiveTrafficSnapshotHandler (REST, on-demand) and TrafficBroadcastService (SignalR push,
/// every 5s) so the two can never independently drift out of sync with each other.
/// </summary>
public static class TrafficResponseMapping
{
    public static LiveTrafficSnapshotResponse ToResponse(this TrafficPresenceSnapshot snapshot) =>
        new(
            TotalActive: snapshot.TotalActive,
            GuestCount: snapshot.GuestCount,
            RoleCounts: snapshot.RoleCounts,
            Visitors: snapshot.Visitors
                .Select(v => new LiveVisitorResponse(
                    v.VisitorId, v.Role, v.StudioId, v.StudioName,
                    v.CountryCode, v.City, v.Latitude, v.Longitude,
                    v.DeviceType, v.Browser, v.Path, v.ConnectedAt))
                .ToList());
}
