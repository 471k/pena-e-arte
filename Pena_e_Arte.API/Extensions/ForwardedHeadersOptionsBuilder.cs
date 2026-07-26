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
        ForwardedHeadersOptions options = new()
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
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

        System.Net.IPNetwork parsed;
        try
        {
            parsed = System.Net.IPNetwork.Parse(trustedProxyCidr);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"ForwardedHeaders:TrustedProxyCidr value '{trustedProxyCidr}' is not a valid " +
                "CIDR (e.g. \"10.0.0.0/8\").", ex);
        }

        options.KnownNetworks.Add(new IPNetwork(parsed.BaseAddress, parsed.PrefixLength));
        return options;
    }
}
