using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Wraps MaxMind.GeoIP2's DatabaseReader over a local .mmdb file (GeoLite2-City —
/// see docs/claude/architecture.md's "Live Traffic Analytics" Decisions Log entry).
/// DatabaseReader is thread-safe and reused as a singleton (per MaxMind's own docs).
/// Degrades to always-null gracefully if GeoIp:DatabasePath is unset or the file is
/// missing/unreadable — this must never throw or block ingestion.
/// </summary>
public class GeoIpService : IGeoIpService, IDisposable
{
    private readonly DatabaseReader? _reader;
    private readonly ILogger<GeoIpService> _logger;

    public GeoIpService(IConfiguration config, ILogger<GeoIpService> logger)
    {
        _logger = logger;
        string? path = config["GeoIp:DatabasePath"];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _logger.LogWarning(
                "GeoIp:DatabasePath not configured or file not found — traffic events will have no country/city data until this is set up. See docs/claude/architecture.md 'Live Traffic Analytics' entry.");
            return;
        }

        try
        {
            _reader = new DatabaseReader(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open GeoIP database at {@Path}", path);
        }
    }

    public GeoIpResult? Lookup(System.Net.IPAddress ip)
    {
        if (_reader is null) return null;
        if (System.Net.IPAddress.IsLoopback(ip) || IsPrivateRange(ip)) return null;

        try
        {
            var city = _reader.City(ip);
            return new GeoIpResult(
                city.Country.IsoCode,
                city.Country.Name,
                city.MostSpecificSubdivision.Name,
                city.City.Name);
        }
        catch (AddressNotFoundException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GeoIP lookup failed for a request — degrading to no location data");
            return null;
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

    public void Dispose() => _reader?.Dispose();
}
