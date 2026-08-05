namespace Pena_e_Arte.Contracts.Responses;

public record LiveTrafficSnapshotResponse(
    int TotalActive,
    int GuestCount,
    Dictionary<string, int> RoleCounts,
    List<LiveVisitorResponse> Visitors);

public record LiveVisitorResponse(
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

public record TrafficHistoryResponse(int Days, List<TrafficHistoryDataPoint> DataPoints);

public record TrafficHistoryDataPoint(
    DateOnly Date,
    int GuestCount,
    int ClientCount,
    int ArtistCount,
    int OwnerCount,
    int IssuerCount);

public record TrafficBreakdownResponse(
    int Days,
    List<TrafficCountryCount> TopCountries,
    List<TrafficNamedCount> DeviceBreakdown,
    List<TrafficNamedCount> BrowserBreakdown,
    List<TrafficNamedCount> TopPages,
    List<TrafficNamedCount> TopNetworks);

public record TrafficCountryCount(string? CountryCode, string? Country, int Count);

public record TrafficNamedCount(string Name, int Count);
