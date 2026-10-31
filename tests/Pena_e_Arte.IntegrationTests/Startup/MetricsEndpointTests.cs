using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Pena_e_Arte.API.Extensions;

namespace Pena_e_Arte.IntegrationTests.Startup;

// Prometheus scraped /metrics as "up" for weeks while the page was empty (200, no body): the
// exporter answers that way when it has nothing to show. Builds a host with the API's own
// AddApiOpenTelemetry + MapPrometheusScrapingEndpoint so the scrape page is checked for real.
public class MetricsEndpointTests
{
    [Fact]
    public async Task MetricsPage_ShowsRequestAndRuntimeMetrics()
    {
        await using WebApplication app = await StartAppAsync();
        HttpClient client = app.GetTestClient();

        (await client.GetAsync("/ping")).StatusCode.Should().Be(HttpStatusCode.OK);

        string body = await ScrapeUntilAsync(client, "http_server_request_duration");

        body.Should().Contain("http_server_request_duration");
        body.Should().Contain("dotnet_thread_pool_queue_length");
        body.Should().Contain("dotnet_thread_pool_thread_count");
    }

    private static async Task<WebApplication> StartAppAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        builder.Services.AddApiOpenTelemetry(configuration);

        WebApplication app = builder.Build();
        app.MapGet("/ping", () => Results.Ok());
        app.MapPrometheusScrapingEndpoint();
        await app.StartAsync();
        return app;
    }

    // The exporter caches a scrape for a short time and request metrics are recorded when the
    // response completes, so the first scrape can miss the /ping that just ran.
    private static async Task<string> ScrapeUntilAsync(HttpClient client, string expected)
    {
        string body = "";
        for (int attempt = 0; attempt < 20 && !body.Contains(expected); attempt++)
        {
            if (attempt > 0) await Task.Delay(250);
            body = await client.GetStringAsync("/metrics");
        }
        return body;
    }
}
