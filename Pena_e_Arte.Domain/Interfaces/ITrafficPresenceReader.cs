namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// Reads current live-visitor presence from Redis (see TrafficBroadcastService's key scheme
/// comment for the exact structure). Shared by the on-demand GetLiveTrafficSnapshotQuery
/// (initial page load, before the first SignalR push arrives) and TrafficBroadcastService's
/// periodic broadcast loop, so the two never drift out of sync.
/// </summary>
public interface ITrafficPresenceReader
{
    Task<TrafficPresenceSnapshot> ReadSnapshotAsync(CancellationToken ct = default);
}

public record TrafficPresenceSnapshot(
    int TotalActive,
    int GuestCount,
    Dictionary<string, int> RoleCounts,
    List<TrafficPresenceVisitor> Visitors);

public record TrafficPresenceVisitor(
    string VisitorId,
    string? Role,
    string? StudioId,
    string? StudioName,
    string? CountryCode,
    string? City,
    double? Latitude,
    double? Longitude,
    string? DeviceType,
    string? Browser,
    string Path,
    DateTime ConnectedAt);
