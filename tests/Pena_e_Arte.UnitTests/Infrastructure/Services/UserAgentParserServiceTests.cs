using FluentAssertions;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.UnitTests.Infrastructure.Services;

public class UserAgentParserServiceTests
{
    private const string ChromeDesktopWindows =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36";

    private const string SafariIphone =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1";

    private const string Googlebot =
        "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)";

    private readonly UserAgentParserService _sut = new();

    [Fact]
    public void Parse_ChromeOnWindowsDesktop_ReturnsDesktopChromeWindows()
    {
        (string? deviceType, string? browser, string? os) = _sut.Parse(ChromeDesktopWindows);

        deviceType.Should().Be("desktop");
        browser.Should().Be("Chrome");
        os.Should().Be("Windows");
    }

    [Fact]
    public void Parse_SafariOniPhone_ReturnsMobileDeviceType()
    {
        var (deviceType, browser, _) = _sut.Parse(SafariIphone);

        deviceType.Should().Be("mobile");
        browser.Should().Be("Mobile Safari");
    }

    [Fact]
    public void Parse_KnownBotUserAgent_ReturnsBotDeviceType()
    {
        var (deviceType, _, _) = _sut.Parse(Googlebot);

        deviceType.Should().Be("bot");
    }

    [Fact]
    public void Parse_NullOrWhitespaceUserAgent_ReturnsAllNulls()
    {
        (string? deviceType, string? browser, string? os) = _sut.Parse(null);

        deviceType.Should().BeNull();
        browser.Should().BeNull();
        os.Should().BeNull();
    }
}
