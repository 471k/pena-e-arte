using FluentAssertions;
using Pena_e_Arte.Infrastructure.Services.ExchangeRates;

namespace Pena_e_Arte.UnitTests.ExchangeRates;

public class BankOfAlbaniaRateParserTests
{
    // The real BoA official rate page as fetched on 2026-10-26 (it showed the rate of Friday 23 Oct),
    // minus its <script> elements (Cloudflare beacon and challenge values), which the parser never reads.
    private static readonly string RealPage =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "boa-official-rate-2026-10-26.html"));

    [Fact]
    public void Parse_RealPage_ReadsTheOfficialEuroRateAndItsDate()
    {
        BankOfAlbaniaRate? rate = BankOfAlbaniaRateParser.Parse(RealPage);

        rate.Should().NotBeNull();
        rate!.Rate.Should().Be(90.74m);
        rate.RateDate.Should().Be(new DateOnly(2026, 10, 23));
        // 12:16:19 in Tirane on 23 Oct 2026 is CEST (UTC+2).
        rate.PublishedAtUtc.Should().Be(new DateTime(2026, 10, 23, 10, 16, 19, DateTimeKind.Utc));
    }

    [Fact]
    public void Parse_RealPage_NeverReadsTheBidAskTable()
    {
        // The page's third table also has an EUR row (bid 90.37 / ask 91.09).
        BankOfAlbaniaRateParser.Parse(RealPage)!.Rate.Should().NotBe(90.37m).And.NotBe(91.09m);
    }

    [Fact]
    public void Parse_EuroRowMissingFromTheOfficialTable_ReturnsNull()
    {
        string withoutEuro = RemoveOfficialEuroRow(RealPage);

        BankOfAlbaniaRateParser.Parse(withoutEuro).Should().BeNull();
    }

    [Fact]
    public void Parse_OfficialTableHeaderChanged_ReturnsNullRatherThanFallingBackToAnotherTable()
    {
        string redesigned = RealPage.Replace("Main Currency", "Currency", StringComparison.Ordinal);

        BankOfAlbaniaRateParser.Parse(redesigned).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body>Service temporarily unavailable</body></html>")]
    public void Parse_NotTheRatePage_ReturnsNull(string html) =>
        BankOfAlbaniaRateParser.Parse(html).Should().BeNull();

    [Fact]
    public void Parse_HandMadeMinimalPage_ReadsRateAndDate()
    {
        const string html = """
            <span>Last update:</span>&nbsp;<span><b>02.11.2026</b>&nbsp;&nbsp;<em><b>12:05:00</b></em></span></div>
            <TABLE><TR><TD>Main Currency</TD><TD>Albanian Lek per Foreign Currency Unit</TD></TR>
            <TR><TD nowrap>Euro</TD><TD nowrap>EUR</TD><td align="right" nowrap>91.2</td><td>+0.46</td></TR></TABLE>
            """;

        BankOfAlbaniaRate? rate = BankOfAlbaniaRateParser.Parse(html);

        rate.Should().NotBeNull();
        rate!.Rate.Should().Be(91.2m);
        rate.RateDate.Should().Be(new DateOnly(2026, 11, 2));
        // 2 Nov 2026 is CET (UTC+1).
        rate.PublishedAtUtc.Should().Be(new DateTime(2026, 11, 2, 11, 5, 0, DateTimeKind.Utc));
    }

    private static string RemoveOfficialEuroRow(string html)
    {
        int table = html.IndexOf("Main Currency", StringComparison.Ordinal);
        int euro = html.IndexOf("<TD nowrap>Euro</TD>", table, StringComparison.Ordinal);
        int rowStart = html.LastIndexOf("<TR>", euro, StringComparison.Ordinal);
        int rowEnd = html.IndexOf("</TR>", euro, StringComparison.Ordinal) + "</TR>".Length;
        return html.Remove(rowStart, rowEnd - rowStart);
    }
}
