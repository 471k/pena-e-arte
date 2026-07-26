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

    private static async Task<IHost> BuildHost(string? trustedProxyCidr, string immediatePeer)
    {
        Dictionary<string, string?> configValues = [];
        if (trustedProxyCidr is not null)
            configValues["ForwardedHeaders:TrustedProxyCidr"] = trustedProxyCidr;

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
