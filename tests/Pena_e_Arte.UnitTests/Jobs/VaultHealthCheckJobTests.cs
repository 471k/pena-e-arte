using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.UnitTests.Jobs;

/// <summary>
/// C-8: the 5-minute Vault health probe logs one "VaultHealthCheckFailed" error line (the Grafana
/// alert's trigger) for any state other than healthy, and stays silent otherwise.
/// </summary>
public class VaultHealthCheckJobTests
{
    private readonly CapturingLogger _logger = new();
    private readonly List<Uri> _requests = [];

    private VaultHealthCheckJob CreateSut(Func<HttpResponseMessage> respond, string address = "http://vault:8200")
    {
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(VaultHealthCheckJob.HttpClientName)
            .Returns(_ => new HttpClient(new StubHandler(respond, _requests)));
        return new VaultHealthCheckJob(factory, Options.Create(new VaultOptions { Address = address }), _logger);
    }

    [Fact]
    public async Task Healthy_LogsNothing()
    {
        await CreateSut(() => new HttpResponseMessage(HttpStatusCode.OK)).RunAsync();

        _requests.Should().ContainSingle().Which.PathAndQuery.Should().Be("/v1/sys/health?standbyok=true");
        _logger.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "sealed")]
    [InlineData(HttpStatusCode.NotImplemented, "not initialised")]
    [InlineData(HttpStatusCode.InternalServerError, "unhealthy")]
    public async Task NotHealthy_LogsOneErrorWithTheAlertMarker(HttpStatusCode status, string expectedState)
    {
        await CreateSut(() => new HttpResponseMessage(status)).RunAsync();

        LogEntry entry = _logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().StartWith("VaultHealthCheckFailed: Vault is " + expectedState);
    }

    [Fact]
    public async Task Unreachable_LogsTheFailure()
    {
        await CreateSut(() => throw new HttpRequestException("connection refused")).RunAsync();

        _logger.Entries.Should().ContainSingle()
            .Which.Message.Should().Contain("VaultHealthCheckFailed: Vault is unreachable (HttpRequestException)");
    }

    [Fact]
    public async Task NoVaultConfigured_SkipsWithoutAnyCall()
    {
        await CreateSut(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), address: "").RunAsync();

        _requests.Should().BeEmpty();
        _logger.Entries.Should().BeEmpty();
    }

    private sealed class StubHandler(Func<HttpResponseMessage> respond, List<Uri> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(request.RequestUri!);
            return Task.FromResult(respond());
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger : ILogger<VaultHealthCheckJob>
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
    }
}
