using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Pena_e_Arte.API.Extensions;

public static class ForwardedHeadersOptionsBuilder
{
    // Historically, an empty KnownNetworks/KnownProxies meant "trust every proxy" (any direct
    // client could set its own X-Forwarded-For and defeat the IP-keyed rate limiter in
    // RateLimitingExtensions.cs). As of the ForwardedHeadersMiddleware security patch shipped in
    // .NET 8.0.17/9.0.6 (carried into this project's net10.0 SDK), that default flipped: an empty
    // KnownNetworks/KnownProxies now means the header is ignored entirely, not trusted from
    // everyone — verified empirically in ForwardedHeadersTests, since older docs/audits (written
    // against the pre-patch behavior) describe the opposite. Leaving TrustedProxyCidr unset is
    // therefore no longer a spoofing hole, but it is still a functional gap: every real client
    // behind the production ingress would share the ingress's own RemoteIpAddress, collapsing
    // them into one rate-limit bucket. TrustedProxyCidr is configurable rather than hardcoded
    // since the actual K3s ingress CIDR isn't knowable from this repo.
    public static ForwardedHeadersOptions BuildForwardedHeadersOptions(
        IConfiguration config, ILogger logger)
    {
        // Hops between the real client and this process in the K3s topology: Cloudflare's edge,
        // Traefik (cluster ingress), then the frontend Pod's own nginx reverse proxy — three
        // entries that get appended to X-Forwarded-For after the client's own address (see
        // docs/claude/overnight-prompt-k3s-production-deploy-2026-07-26.md §8.10/Phase 10).
        // ForwardLimit bounds how many of them are stripped: too low and RemoteIpAddress
        // resolves to a proxy instead of the real client, collapsing every client behind the
        // ingress into one rate-limit bucket and leaving GeoIP nothing public to look up.
        // Default 2 keeps the pre-Cloudflare topology (Traefik + nginx) working when the setting
        // is absent, e.g. docker-compose.
        int forwardLimit = config.GetValue<int?>("ForwardedHeaders:ForwardLimit") ?? 2;
        if (forwardLimit < 1)
        {
            throw new InvalidOperationException(
                $"ForwardedHeaders:ForwardLimit value '{forwardLimit}' must be at least 1.");
        }

        ForwardedHeadersOptions options = new()
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = forwardLimit,
        };

        string? trustedProxyCidr = config["ForwardedHeaders:TrustedProxyCidr"];
        if (string.IsNullOrWhiteSpace(trustedProxyCidr))
        {
            logger.LogWarning(
                "ForwardedHeaders:TrustedProxyCidr is not set — X-Forwarded-For will be ignored " +
                "(the .NET runtime no longer trusts it by default without a configured known " +
                "proxy/network), so every client behind the real ingress will share one " +
                "rate-limit bucket keyed on the ingress's own IP. Set this in production to the " +
                "ingress/load-balancer CIDR.");
            return options;
        }

        AddTrustedNetworks(options, trustedProxyCidr, "ForwardedHeaders:TrustedProxyCidr");

        // Proxies that sit in front of the cluster and must also be peeled off the
        // X-Forwarded-For chain to reach the real client — Cloudflare's published edge ranges.
        // A separate, non-secret list (it lives in the ConfigMap, not the CD-populated Secret)
        // so the ranges are reviewable in the repo. Trusting them here is safe only because the
        // ingress itself trusts Cloudflare's forwarded headers and overwrites anything sent by a
        // direct, non-Cloudflare client (k8s/cluster/traefik-helmchartconfig.yaml).
        AddTrustedNetworks(options, config["ForwardedHeaders:ExtraTrustedCidrs"], "ForwardedHeaders:ExtraTrustedCidrs");
        return options;
    }

    /// <summary>Adds every CIDR in a comma-, semicolon- or whitespace-separated list.</summary>
    private static void AddTrustedNetworks(ForwardedHeadersOptions options, string? cidrList, string configKey)
    {
        if (string.IsNullOrWhiteSpace(cidrList))
            return;

        string[] cidrs = cidrList.Split(
            [',', ';', ' ', '\r', '\n', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string cidr in cidrs)
        {
            System.Net.IPNetwork parsed;
            try
            {
                parsed = System.Net.IPNetwork.Parse(cidr);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"{configKey} entry '{cidr}' is not a valid CIDR (e.g. \"10.0.0.0/8\").", ex);
            }

            options.KnownNetworks.Add(new IPNetwork(parsed.BaseAddress, parsed.PrefixLength));
        }
    }
}
