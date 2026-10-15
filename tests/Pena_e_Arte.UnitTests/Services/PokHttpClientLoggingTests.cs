using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Infrastructure.Extensions;

namespace Pena_e_Arte.UnitTests.Services;

/// <summary>
/// C-17 (POK staging gate, 2026-10-14): the "Pok" named HttpClient must not write request URIs to
/// the logs — POK's paths carry the merchant id and full order ids.
/// </summary>
public class PokHttpClientLoggingTests
{
    private const string MerchantId = "merchant-0000-secret-id";
    private const string OrderId = "11111111-2222-3333-4444-555555555555";
    private static readonly Uri OrderUrl = new($"https://api-staging.pokpay.io/merchants/{MerchantId}/sdk-orders/{OrderId}");

    [Fact]
    public async Task Pok_client_writes_no_log_line_with_the_request_uri()
    {
        CapturingLoggerProvider logs = new();
        ServiceCollection services = NewServices(logs);
        services.AddPokHttpClient();
        services.AddHttpClient("Pok").ConfigurePrimaryHttpMessageHandler(() => new OkHandler());

        await using ServiceProvider provider = services.BuildServiceProvider();
        HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("Pok");
        HttpResponseMessage response = await client.GetAsync(OrderUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        logs.Entries.Should().NotContain(e => e.Category.StartsWith("System.Net.Http.HttpClient.Pok"));
        logs.Entries.Should().NotContain(e => e.Message.Contains(MerchantId) || e.Message.Contains(OrderId));
    }

    [Fact]
    public async Task A_default_named_client_does_log_the_uri_so_the_capture_above_is_meaningful()
    {
        CapturingLoggerProvider logs = new();
        ServiceCollection services = NewServices(logs);
        services.AddHttpClient("Other").ConfigurePrimaryHttpMessageHandler(() => new OkHandler());

        await using ServiceProvider provider = services.BuildServiceProvider();
        HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("Other");
        await client.GetAsync(OrderUrl);

        logs.Entries.Should().Contain(e => e.Category.StartsWith("System.Net.Http.HttpClient.Other") && e.Message.Contains(MerchantId));
    }

    private static ServiceCollection NewServices(CapturingLoggerProvider logs)
    {
        ServiceCollection services = new();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        return services;
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed record LogEntry(string Category, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new LogEntry(category, formatter(state, exception)));
    }
}
