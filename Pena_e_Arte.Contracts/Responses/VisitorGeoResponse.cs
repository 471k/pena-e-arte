namespace Pena_e_Arte.Contracts.Responses;

/// <summary>Where the visitor's IP resolves to, for form defaults only. CountryCode is ISO 3166-1
/// alpha-2 and TimeZone an IANA id (for example "Europe/Tirane"); each is null when it can't be
/// resolved (private/local address, no GeoIP database, unknown range), and the caller then falls
/// back to its own guess.</summary>
public record VisitorGeoResponse(string? CountryCode, string? TimeZone);
