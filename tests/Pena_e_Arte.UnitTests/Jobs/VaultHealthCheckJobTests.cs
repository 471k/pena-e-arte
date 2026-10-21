using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.UnitTests.Jobs;

/// <summary>
/// C-8: the 5-minute Vault health probe logs one "VaultHealthCheckFailed" error line (the Grafana
/// alert's trigger) for any state other than healthy, and stays silent otherwise. With a token
/// configured it also checks the app's own token (expired → alert; under half its period left →
/// renewed) and its permissions on the studios' POK key paths.
/// </summary>
public class VaultHealthCheckJobTests
{
    private const string Token = "hvs.test-token-not-real";
    private const string ProbePath = "secret/data/studios/00000000-0000-0000-0000-000000000000/pok";

    private readonly CapturingLogger _logger = new();
    private readonly List<RecordedRequest> _requests = [];

    private VaultHealthCheckJob CreateSut(Func<HttpRequestMessage, HttpResponseMessage> respond, string address = "http://vault:8200", string token = "")
    {
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(VaultHealthCheckJob.HttpClientName)
            .Returns(_ => new HttpClient(new StubHandler(respond, _requests)));
        return new VaultHealthCheckJob(factory, Options.Create(new VaultOptions { Address = address, Token = token }), _logger);
    }

    private VaultHealthCheckJob CreateSut(Func<HttpResponseMessage> respond, string address = "http://vault:8200") =>
        CreateSut(_ => respond(), address);

    // A healthy Vault that answers the token endpoints like a real one; each part can be overridden.
    private static Func<HttpRequestMessage, HttpResponseMessage> Vault(
        HttpResponseMessage? lookup = null, HttpResponseMessage? renew = null, string[]? capabilities = null) => request =>
        request.RequestUri!.AbsolutePath switch
        {
            "/v1/sys/health" => new HttpResponseMessage(HttpStatusCode.OK),
            "/v1/auth/token/lookup-self" => lookup ?? Lookup(ttl: 2_000_000, period: 2_592_000),
            "/v1/auth/token/renew-self" => renew ?? Json("""{"auth":{"lease_duration":2592000}}"""),
            "/v1/sys/capabilities-self" => Capabilities(capabilities ?? ["create", "patch", "read", "update"]),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };

    private static HttpResponseMessage Lookup(long ttl, long period, bool renewable = true) =>
        Json($$$"""{"data":{"ttl":{{{ttl}}},"period":{{{period}}},"renewable":{{{(renewable ? "true" : "false")}}},"policies":["default","tattooos-app"]}}""");

    private static HttpResponseMessage Capabilities(string[] granted)
    {
        string list = string.Join(",", granted.Select(c => $"\"{c}\""));
        return Json($$$"""{"capabilities":[{{{list}}}],"{{{ProbePath}}}":[{{{list}}}],"data":{"capabilities":[{{{list}}}],"{{{ProbePath}}}":[{{{list}}}]}}""");
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

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

    [Fact]
    public async Task ValidTokenWithFullPermissions_LogsNothingAndDoesNotRenew()
    {
        await CreateSut(Vault(), token: Token).RunAsync();

        _requests.Select(r => r.Path).Should().Equal("/v1/sys/health", "/v1/auth/token/lookup-self", "/v1/sys/capabilities-self");
        _requests.Skip(1).Should().OnlyContain(r => r.TokenHeader == Token);
        _requests[2].Body.Should().Contain(ProbePath);
        _logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task SealedVault_DoesNotCheckTheToken()
    {
        await CreateSut(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable), token: Token).RunAsync();

        _requests.Should().ContainSingle();
        _logger.Entries.Should().ContainSingle().Which.Message.Should().StartWith("VaultHealthCheckFailed: Vault is sealed");
    }

    [Fact]
    public async Task ExpiredToken_Alerts()
    {
        await CreateSut(Vault(lookup: new HttpResponseMessage(HttpStatusCode.Forbidden)), token: Token).RunAsync();

        LogEntry entry = _logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().StartWith("VaultHealthCheckFailed: Vault is rejecting the app's token (invalid or expired) (HTTP 403)");
    }

    [Fact]
    public async Task LessThanHalfThePeriodLeft_RenewsTheToken()
    {
        await CreateSut(Vault(lookup: Lookup(ttl: 1_000_000, period: 2_592_000)), token: Token).RunAsync();

        _requests.Should().Contain(r => r.Path == "/v1/auth/token/renew-self" && r.Method == "POST" && r.TokenHeader == Token);
        LogEntry entry = _logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.Message.Should().StartWith("VaultTokenRenewed: the app's Vault token had 277 h left and was renewed for another 720 h");
    }

    [Fact]
    public async Task RenewalRefused_Alerts()
    {
        await CreateSut(
            Vault(lookup: Lookup(ttl: 1_000, period: 2_592_000), renew: new HttpResponseMessage(HttpStatusCode.Forbidden)),
            token: Token).RunAsync();

        _logger.Entries.Should().ContainSingle()
            .Which.Message.Should().StartWith("VaultHealthCheckFailed: Vault is refusing to renew the app's token (HTTP 403)");
    }

    [Fact]
    public async Task NonExpiringToken_IsNotRenewed()
    {
        // The local dev root token: ttl 0, no period, not renewable.
        await CreateSut(Vault(lookup: Lookup(ttl: 0, period: 0, renewable: false), capabilities: ["root"]), token: Token).RunAsync();

        _requests.Should().NotContain(r => r.Path == "/v1/auth/token/renew-self");
        _logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadOnlyPolicy_AlertsWithTheMissingPermissions()
    {
        await CreateSut(Vault(capabilities: ["read"]), token: Token).RunAsync();

        _logger.Entries.Should().ContainSingle()
            .Which.Message.Should().StartWith("VaultHealthCheckFailed: Vault is missing permissions on the studios' POK keys (create, update, patch)");
    }

    [Fact]
    public async Task NoLogLineEverContainsTheToken()
    {
        await CreateSut(Vault(lookup: Lookup(ttl: 10, period: 2_592_000), capabilities: ["deny"]), token: Token).RunAsync();

        _logger.Entries.Should().NotBeEmpty();
        _logger.Entries.Should().OnlyContain(e => !e.Message.Contains(Token));
    }

    private sealed record RecordedRequest(string Method, string Path, string PathAndQuery, string? TokenHeader, string Body);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond, List<RecordedRequest> requests) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            string? token = request.Headers.TryGetValues("X-Vault-Token", out IEnumerable<string>? values) ? values.Single() : null;
            requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!.AbsolutePath, request.RequestUri.PathAndQuery, token, body));
            return respond(request);
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
