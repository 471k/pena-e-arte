namespace Pena_e_Arte.Domain.Interfaces;

public interface IGeoIpService
{
    GeoIpResult? Lookup(System.Net.IPAddress ip);
}

/// <summary>
/// RegionCode is the subdivision's ISO code (Region keeps the subdivision name, as before).
/// PostalCode/ContinentCode/Continent/AccuracyRadiusKm/TimeZone are captured because they're
/// free data on the same City() lookup, but deliberately never rendered in any UI — see
/// architecture.md's "Live traffic analytics — GeoIP field scope" Decisions Log entry. Postal
/// code in particular is materially more identifying than city and would break the existing
/// "never more precise than rough location" promise in Help copy if shown per-visitor.
/// AsnNumber/AsnOrganization come from a separate GeoLite2-ASN lookup, not City().
/// </summary>
public record GeoIpResult(
    string? CountryCode,
    string? Country,
    string? RegionCode,
    string? Region,
    string? City,
    string? PostalCode,
    string? ContinentCode,
    string? Continent,
    double? Latitude,
    double? Longitude,
    int? AccuracyRadiusKm,
    string? TimeZone,
    long? AsnNumber,
    string? AsnOrganization);
