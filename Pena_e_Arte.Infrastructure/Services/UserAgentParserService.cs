using Pena_e_Arte.Domain.Interfaces;
using UAParser;
using UAParser.Objects;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Wraps UAParser.Core (the same ua-parser ruleset family Umami/Plausible/PostHog use for
/// device/browser/OS classification). The underlying Parser is stateless/thread-safe, so it's
/// safe to reuse as a singleton field rather than constructing one per call.
/// </summary>
public class UserAgentParserService : IUserAgentParser
{
    private readonly Parser _parser = Parser.GetDefault();

    public (string? DeviceType, string? Browser, string? Os) Parse(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return (null, null, null);

        ClientInfo info = _parser.Parse(userAgent);

        // Real analytics tools separate bot/crawler traffic from human visitors so it never
        // inflates an "active now" count — UAParser flags this via Device.IsSpider. No
        // structured DeviceType enum exists on Device in this package, so bucket from the
        // free-text Family string (e.g. "iPhone", "Samsung SM-G960F", "Spider", "Other").
        string deviceType = info.Device.IsSpider || info.Device.Family == "Spider"
            ? "bot"
            : info.Device.Family switch
            {
                "Other" => "desktop",
                var family when family.Contains("Tablet", StringComparison.OrdinalIgnoreCase)
                             || family.Contains("iPad", StringComparison.OrdinalIgnoreCase) => "tablet",
                var family when IsMobileFamily(family) => "mobile",
                _ => "desktop",
            };

        return (deviceType, info.Browser.Family, info.OS.Family);
    }

    private static bool IsMobileFamily(string family) =>
        family.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
        family.Contains("Android", StringComparison.OrdinalIgnoreCase) ||
        family.Contains("Mobile", StringComparison.OrdinalIgnoreCase);
}
