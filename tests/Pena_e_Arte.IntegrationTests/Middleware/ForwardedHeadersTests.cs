using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Pena_e_Arte.API.Extensions;

namespace Pena_e_Arte.IntegrationTests.Middleware;

public class ForwardedHeadersTests
{
    [Fact]
    public async Task TrustedProxyCidrUnset_ForwardedForIgnored_RemoteIpReflectsImmediatePeer()
    {
        // As of the ForwardedHeadersMiddleware security patch shipped in .NET 8.0.17/9.0.6
        // (carried into net10.0, the SDK this project targets), an empty KnownNetworks/
        // KnownProxies no longer means "trust every proxy" the way the pre-patch behavior (and
        // this file's original code comment) assumed — it means the header is ignored entirely.
        // So with TrustedProxyCidr unset, a spoofed X-Forwarded-For is NOT honored today; the
        // functional gap is the opposite of Finding 2's framing: rate limiting instead falls
        // back to keying off the immediate peer (the ingress, in production), so every real
        // client behind that ingress would share one bucket — which is exactly what setting
        // TrustedProxyCidr correctly fixes on both counts.
        using IHost host = await BuildHost(trustedProxyCidr: null, immediatePeer: "198.51.100.9");
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();

        HttpRequestMessage request = new(HttpMethod.Get, "/ip");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        HttpResponseMessage response = await client.SendAsync(request);

        (await response.Content.ReadAsStringAsync()).Should().Be("198.51.100.9");
    }

    [Fact]
    public async Task TrustedProxyCidrConfigured_ForwardedForFromUntrustedPeer_RemoteIpNotRewritten()
    {
        using IHost host = await BuildHost(trustedProxyCidr: "10.0.0.0/8", immediatePeer: "198.51.100.9");
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();

        HttpRequestMessage request = new(HttpMethod.Get, "/ip");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        HttpResponseMessage response = await client.SendAsync(request);

        (await response.Content.ReadAsStringAsync()).Should().Be("198.51.100.9");
    }

    [Fact]
    public async Task TrustedProxyCidrConfigured_ForwardedForFromPeerWithinCidr_RemoteIpRewritten()
    {
        using IHost host = await BuildHost(trustedProxyCidr: "10.0.0.0/8", immediatePeer: "10.1.2.3");
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();

        HttpRequestMessage request = new(HttpMethod.Get, "/ip");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        HttpResponseMessage response = await client.SendAsync(request);

        (await response.Content.ReadAsStringAsync()).Should().Be("203.0.113.7");
    }

    [Fact]
    public async Task TrustedProxyCidrConfigured_TwoHopChainBothWithinCidr_RemoteIpIsRealClientNotFirstHop()
    {
        // Regression test for the K3s topology's two in-cluster proxy hops (Traefik, then the
        // frontend Pod's own nginx) — see ForwardedHeadersOptionsBuilder's ForwardLimit: 2.
        // With the old default ForwardLimit of 1, only the right-most entry gets processed and
        // RemoteIpAddress would incorrectly resolve to "10.42.1.5" (the first hop) instead of
        // the real client, defeating per-client rate limiting.
        using IHost host = await BuildHost(trustedProxyCidr: "10.42.0.0/16", immediatePeer: "10.42.3.3");
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();

        HttpRequestMessage request = new(HttpMethod.Get, "/ip");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7, 10.42.1.5");
        HttpResponseMessage response = await client.SendAsync(request);

        (await response.Content.ReadAsStringAsync()).Should().Be("203.0.113.7");
    }

    [Fact]
    public async Task CloudflareChain_ThreeHopsWithExtraCidrsAndForwardLimitThree_RemoteIpIsRealClient()
    {
        // Production topology once Traefik trusts Cloudflare's forwarded headers: the API sees
        // "client, cloudflare-edge, traefik-pod" from the frontend nginx pod. Without the extra
        // Cloudflare CIDR the middleware stops at the (untrusted) edge address and resolves
        // RemoteIpAddress to Cloudflare instead of the visitor.
        using IHost host = await BuildHost(
            trustedProxyCidr: "10.42.0.0/16",
            immediatePeer: "10.42.3.3",
            extraTrustedCidrs: "173.245.48.0/20,104.16.0.0/13,2606:4700::/32",
            forwardLimit: "3");
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();

        HttpRequestMessage request = new(HttpMethod.Get, "/ip");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7, 104.16.5.5, 10.42.1.5");
        HttpResponseMessage response = await client.SendAsync(request);

        (await response.Content.ReadAsStringAsync()).Should().Be("203.0.113.7");
    }

    [Fact]
    public async Task CloudflareChain_ClientSuppliedLeadingForwardedForEntry_IsNotHonored()
    {
        // A visitor can send their own X-Forwarded-For through Cloudflare, which appends the
        // real address after it. Only the right-most entries (real proxies) may be stripped, so
        // the spoofed leading value must never become RemoteIpAddress.
        using IHost host = await BuildHost(
            trustedProxyCidr: "10.42.0.0/16",
            immediatePeer: "10.42.3.3",
            extraTrustedCidrs: "104.16.0.0/13",
            forwardLimit: "3");
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();

        HttpRequestMessage request = new(HttpMethod.Get, "/ip");
        request.Headers.Add("X-Forwarded-For", "192.0.2.99, 203.0.113.7, 104.16.5.5, 10.42.1.5");
        HttpResponseMessage response = await client.SendAsync(request);

        (await response.Content.ReadAsStringAsync()).Should().Be("203.0.113.7");
    }

    [Fact]
    public async Task CloudflareChain_WithoutExtraCidrs_StopsAtCloudflareEdge()
    {
        // Documents why ExtraTrustedCidrs is needed: with only the pod CIDR trusted, the
        // middleware cannot peel the Cloudflare edge address off the chain.
        using IHost host = await BuildHost(
            trustedProxyCidr: "10.42.0.0/16",
            immediatePeer: "10.42.3.3",
            forwardLimit: "3");
        using TestServer server = host.GetTestServer();
        using HttpClient client = server.CreateClient();

        HttpRequestMessage request = new(HttpMethod.Get, "/ip");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7, 104.16.5.5, 10.42.1.5");
        HttpResponseMessage response = await client.SendAsync(request);

        (await response.Content.ReadAsStringAsync()).Should().Be("104.16.5.5");
    }

    [Fact]
    public void ExtraTrustedCidrsInvalid_Throws()
    {
        Dictionary<string, string?> values = new()
        {
            ["ForwardedHeaders:TrustedProxyCidr"] = "10.42.0.0/16",
            ["ForwardedHeaders:ExtraTrustedCidrs"] = "104.16.0.0/13,not-a-cidr",
        };
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Action act = () => ForwardedHeadersOptionsBuilder.BuildForwardedHeadersOptions(config, NullLogger.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not-a-cidr*");
    }

    [Fact]
    public void ForwardLimitBelowOne_Throws()
    {
        Dictionary<string, string?> values = new() { ["ForwardedHeaders:ForwardLimit"] = "0" };
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Action act = () => ForwardedHeadersOptionsBuilder.BuildForwardedHeadersOptions(config, NullLogger.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*at least 1*");
    }

    private static async Task<IHost> BuildHost(
        string? trustedProxyCidr,
        string immediatePeer,
        string? extraTrustedCidrs = null,
        string? forwardLimit = null)
    {
        Dictionary<string, string?> configValues = [];
        if (trustedProxyCidr is not null)
            configValues["ForwardedHeaders:TrustedProxyCidr"] = trustedProxyCidr;
        if (extraTrustedCidrs is not null)
            configValues["ForwardedHeaders:ExtraTrustedCidrs"] = extraTrustedCidrs;
        if (forwardLimit is not null)
            configValues["ForwardedHeaders:ForwardLimit"] = forwardLimit;

        IHostBuilder builder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(configValues));
                webBuilder.Configure((ctx, app) =>
                {
                    // Simulates the real TCP peer address Kestrel would report — the last hop
                    // before this process (e.g. the ingress), which is what ForwardedHeadersMiddleware
                    // checks against KnownNetworks before it will honor X-Forwarded-For at all.
                    app.Use(async (context, next) =>
                    {
                        context.Features.Set<IHttpConnectionFeature>(new HttpConnectionFeature
                        {
                            RemoteIpAddress = IPAddress.Parse(immediatePeer),
                        });
                        await next();
                    });
                    app.UseForwardedHeaders(ForwardedHeadersOptionsBuilder.BuildForwardedHeadersOptions(
                        ctx.Configuration, NullLogger.Instance));
                    app.Run(async context =>
                    {
                        await context.Response.WriteAsync(
                            context.Connection.RemoteIpAddress?.ToString() ?? string.Empty);
                    });
                });
            });

        return await builder.StartAsync();
    }
}
