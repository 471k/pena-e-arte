namespace Pena_e_Arte.Domain.Interfaces;

public interface IGeoIpService
{
    GeoIpResult? Lookup(System.Net.IPAddress ip);
}

public record GeoIpResult(string? CountryCode, string? Country, string? Region, string? City);
