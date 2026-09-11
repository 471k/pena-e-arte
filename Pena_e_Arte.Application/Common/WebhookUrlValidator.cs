using System.Net;

namespace Pena_e_Arte.Application.Common;

/// <summary>
/// Basic SSRF guard for studio-supplied webhook URLs: our server will POST to whatever URL
/// is configured here, so it must never be able to reach loopback/private/link-local/reserved
/// address space (internal services, cloud metadata endpoints, etc.). Requires HTTPS. This
/// checks the literal IP when the host is one, and re-runs at delivery time in
/// WebhookDeliveryJob (not just at save time) since save-time validation alone can't catch
/// DNS rebinding for a hostname that resolved to a public IP when it was configured. A studio
/// pointing this at another *public* internal-looking hostname they don't control is treated
/// as a matter for that host's own auth, not something this guard is responsible for.
/// </summary>
public static class WebhookUrlValidator
{
    private static readonly string[] BlockedIPv4Ranges =
    [
        "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16",
        "172.16.0.0/12", "192.0.0.0/24", "192.0.2.0/24", "192.168.0.0/16",
        "198.18.0.0/15", "198.51.100.0/24", "203.0.113.0/24", "224.0.0.0/4", "240.0.0.0/4",
    ];

    private static readonly string[] BlockedIPv6Ranges =
    [
        "::1/128", "::ffff:0:0/96", "fc00::/7", "fe80::/10", "ff00::/8",
    ];

    private static readonly IPNetwork[] BlockedNetworks =
        [.. BlockedIPv4Ranges.Concat(BlockedIPv6Ranges).Select(IPNetwork.Parse)];

    public static bool IsAllowed(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        if (uri.IsLoopback) return false;

        // Non-literal hostnames (the overwhelming majority) pass this check — resolving DNS
        // synchronously inside a validator isn't worth the complexity for an MVP guard; the
        // delivery-time re-check (IsAllowedAsync) exists precisely to catch a hostname
        // resolving somewhere this would have blocked, at the moment it actually matters.
        if (!IPAddress.TryParse(uri.Host, out IPAddress? ip)) return true;

        return !BlockedNetworks.Any(net => net.Contains(ip));
    }

    /// <summary>
    /// Delivery-time check: resolves the hostname and validates every returned address,
    /// closing the DNS-rebinding gap IsAllowed's literal-IP-only check leaves open. Used
    /// immediately before each outbound POST in WebhookDeliveryJob.
    /// </summary>
    public static async Task<bool> IsAllowedAsync(string url, CancellationToken ct)
    {
        if (!IsAllowed(url)) return false;

        Uri uri = new(url);
        if (IPAddress.TryParse(uri.Host, out _)) return true; // already fully checked above

        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
            return addresses.Length > 0 && addresses.All(ip => !BlockedNetworks.Any(net => net.Contains(ip)));
        }
        catch (System.Net.Sockets.SocketException)
        {
            return false;
        }
    }
}
