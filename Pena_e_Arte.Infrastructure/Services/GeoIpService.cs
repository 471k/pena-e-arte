using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Wraps MaxMind.GeoIP2's DatabaseReader over two local .mmdb files — GeoLite2-City
/// (GeoIp:DatabasePath) and GeoLite2-ASN (GeoIp:AsnDatabasePath), both free MaxMind
/// editions (see docs/claude/architecture.md's "Live Traffic Analytics" Decisions Log
/// entries). DatabaseReader is thread-safe and reused as a singleton (per MaxMind's own
/// docs). Each reader degrades to always-null gracefully, independently of the other, if
/// its config path is unset or the file is missing/unreadable — one database being absent
/// must never block a lookup against the other, and neither must ever throw or block
/// ingestion.
/// </summary>
public class GeoIpService : IGeoIpService, IDisposable
{
    private readonly DatabaseReader? _cityReader;
    private readonly DatabaseReader? _asnReader;
    private readonly ILogger<GeoIpService> _logger;

    public GeoIpService(IConfiguration config, ILogger<GeoIpService> logger)
    {
        _logger = logger;
        _cityReader = OpenReader(config["GeoIp:DatabasePath"], "GeoIp:DatabasePath");
        _asnReader = OpenReader(config["GeoIp:AsnDatabasePath"], "GeoIp:AsnDatabasePath");
    }

    private DatabaseReader? OpenReader(string? path, string configKey)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _logger.LogWarning(
                "{ConfigKey} not configured or file not found — traffic events will be missing the corresponding GeoIP data until this is set up. See docs/claude/architecture.md 'Live Traffic Analytics' entry.",
                configKey);
            return null;
        }

        try
        {
            return new DatabaseReader(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open GeoIP database at {@Path}", path);
            return null;
        }
    }

    public GeoIpResult? Lookup(System.Net.IPAddress ip)
    {
        if (_cityReader is null && _asnReader is null) return null;
        if (System.Net.IPAddress.IsLoopback(ip) || IsPrivateRange(ip)) return null;

        MaxMind.GeoIP2.Responses.CityResponse? city = LookupCity(ip);
        (long? asnNumber, string? asnOrganization) = LookupAsn(ip);

        if (city is null && asnNumber is null) return null;

        return new GeoIpResult(
            CountryCode: city?.Country.IsoCode,
            Country: city?.Country.Name,
            RegionCode: city?.MostSpecificSubdivision.IsoCode,
            Region: city?.MostSpecificSubdivision.Name,
            City: city?.City.Name,
            PostalCode: city?.Postal.Code,
            ContinentCode: city?.Continent.Code,
            Continent: city?.Continent.Name,
            Latitude: city?.Location.Latitude,
            Longitude: city?.Location.Longitude,
            AccuracyRadiusKm: city?.Location.AccuracyRadius,
            TimeZone: city?.Location.TimeZone,
            AsnNumber: asnNumber,
            AsnOrganization: asnOrganization);
    }

    private MaxMind.GeoIP2.Responses.CityResponse? LookupCity(System.Net.IPAddress ip)
    {
        if (_cityReader is null) return null;
        try
        {
            return _cityReader.City(ip);
        }
        catch (AddressNotFoundException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GeoIP City lookup failed for a request — degrading to no location data");
            return null;
        }
    }

    private (long? AsnNumber, string? AsnOrganization) LookupAsn(System.Net.IPAddress ip)
    {
        if (_asnReader is null) return (null, null);
        try
        {
            MaxMind.GeoIP2.Responses.AsnResponse asn = _asnReader.Asn(ip);
            return (asn.AutonomousSystemNumber, asn.AutonomousSystemOrganization);
        }
        catch (AddressNotFoundException)
        {
            return (null, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GeoIP ASN lookup failed for a request — degrading to no network data");
            return (null, null);
        }
    }

    // Checked against ForwardedHeadersOptionsBuilder.cs: RemoteIpAddress resolution there uses
    // System.Net.IPNetwork.Parse, which is already IP-family-agnostic (accepts IPv6 CIDRs), so
    // an IPv6 client IP can legitimately reach here once ForwardedHeaders:TrustedProxyCidr is
    // configured for an IPv6-capable ingress. IsPrivateRange therefore covers both families.
    private static bool IsPrivateRange(System.Net.IPAddress ip)
    {
        // ::ffff:10.0.0.5-style IPv4-mapped addresses report AddressFamily.InterNetworkV6 but
        // carry an embedded IPv4 address — unwrap before range-checking, otherwise a private
        // IPv4 address disguised as IPv6 falls through both branches below undetected.
        if (ip.IsIPv4MappedToIPv6)
            return IsPrivateRange(ip.MapToIPv4());

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            byte[] v6 = ip.GetAddressBytes();
            // fc00::/7 — unique local addresses (RFC 4193); IsLoopback already covers ::1.
            return (v6[0] & 0xFE) == 0xFC;
        }

        byte[] b = ip.GetAddressBytes();
        if (b.Length != 4) return false;
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }

    public void Dispose()
    {
        _cityReader?.Dispose();
        _asnReader?.Dispose();
    }
}
