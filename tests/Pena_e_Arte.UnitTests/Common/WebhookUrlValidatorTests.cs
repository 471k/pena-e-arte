using FluentAssertions;
using Pena_e_Arte.Application.Common;

namespace Pena_e_Arte.UnitTests.Common;

public class WebhookUrlValidatorTests
{
    [Theory]
    [InlineData("https://example.com/webhook")]
    [InlineData("https://api.mystudio-integration.io/hooks/pena-e-arte")]
    [InlineData("https://8.8.8.8/webhook")] // public literal IP
    public void IsAllowed_PublicHttpsUrl_ReturnsTrue(string url) =>
        WebhookUrlValidator.IsAllowed(url).Should().BeTrue();

    [Theory]
    [InlineData("http://example.com/webhook")] // not https
    [InlineData("ftp://example.com/webhook")]
    [InlineData("not-a-url")]
    [InlineData("")]
    [InlineData(null)]
    public void IsAllowed_NonHttpsOrInvalidUrl_ReturnsFalse(string? url) =>
        WebhookUrlValidator.IsAllowed(url).Should().BeFalse();

    [Theory]
    [InlineData("https://localhost/webhook")]
    [InlineData("https://127.0.0.1/webhook")]
    [InlineData("https://10.0.0.5/webhook")]
    [InlineData("https://172.16.0.1/webhook")]
    [InlineData("https://192.168.1.1/webhook")]
    [InlineData("https://169.254.169.254/webhook")] // cloud metadata endpoint
    [InlineData("https://[::1]/webhook")]
    [InlineData("https://[fc00::1]/webhook")]
    [InlineData("https://[fe80::1]/webhook")]
    public void IsAllowed_LoopbackOrPrivateOrLinkLocal_ReturnsFalse(string url) =>
        WebhookUrlValidator.IsAllowed(url).Should().BeFalse();

    [Fact]
    public async Task IsAllowedAsync_PublicLiteralIp_ReturnsTrue() =>
        (await WebhookUrlValidator.IsAllowedAsync("https://8.8.8.8/webhook", default)).Should().BeTrue();

    [Fact]
    public async Task IsAllowedAsync_PrivateLiteralIp_ReturnsFalse() =>
        (await WebhookUrlValidator.IsAllowedAsync("https://10.0.0.5/webhook", default)).Should().BeFalse();

    [Fact]
    public async Task IsAllowedAsync_UnresolvableHostname_ReturnsFalse() =>
        (await WebhookUrlValidator.IsAllowedAsync(
            "https://this-host-does-not-exist.invalid/webhook", default)).Should().BeFalse();
}
