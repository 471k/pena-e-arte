using System.Globalization;
using System.Text.RegularExpressions;

namespace Pena_e_Arte.Infrastructure.Services.ExchangeRates;

/// <summary>One EUR rate read from the Bank of Albania's official exchange rate page.</summary>
/// <param name="Rate">Lek per one euro, as published (e.g. 90.74).</param>
/// <param name="RateDate">The "Last update" date BoA prints above the table (Europe/Tirane).</param>
/// <param name="PublishedAtUtc">The "Last update" date and time, converted from Europe/Tirane to UTC.</param>
public sealed record BankOfAlbaniaRate(decimal Rate, DateOnly RateDate, DateTime PublishedAtUtc);

/// <summary>
/// Reads the EUR row of the official "Fix" rate from https://www.bankofalbania.org/Markets/Official_exchange_rate/
/// with narrow regexes, not an HTML library (no new package, spec D8). The page has three tables, each
/// headed by its own "Last update:": the official rate ("Main Currency | Albanian Lek per Foreign
/// Currency Unit"), a second list of other currencies, and the interbank bid/ask averages, which also
/// have an EUR row. Only the first table counts, and it is recognised by its "Main Currency" header,
/// never by position alone. BoA prints the date and time in separate tags
/// (<c>&lt;b&gt;23.10.2026&lt;/b&gt;&amp;nbsp;&amp;nbsp;&lt;em&gt;&lt;b&gt;12:16:19&lt;/b&gt;&lt;/em&gt;</c>), checked
/// against the live page on 2026-10-26. Returns null when anything doesn't match, so a page redesign
/// stores nothing rather than a wrong number.
/// </summary>
public static class BankOfAlbaniaRateParser
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex LastUpdate = new(
        @"Last update:.{0,300}?(?<date>\d{2}\.\d{2}\.\d{4}).{0,120}?(?<time>\d{2}:\d{2}:\d{2})",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex EuroRow = new(
        @"<td[^>]*>\s*Euro\s*</td>\s*<td[^>]*>\s*EUR\s*</td>\s*<td[^>]*>\s*(?<rate>\d{1,4}(?:\.\d{1,6})?)\s*</td>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);

    public static BankOfAlbaniaRate? Parse(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;

        // The official table: the first "Last update:" block whose table has the "Main Currency" header.
        int start = html.IndexOf("Last update:", StringComparison.OrdinalIgnoreCase);
        while (start >= 0)
        {
            int end = html.IndexOf("</table>", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0) return null;

            string block = html[start..end];
            if (block.Contains("Main Currency", StringComparison.OrdinalIgnoreCase))
                return ParseBlock(block);

            start = html.IndexOf("Last update:", end, StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }

    private static BankOfAlbaniaRate? ParseBlock(string block)
    {
        Match updated = LastUpdate.Match(block);
        Match euro = EuroRow.Match(block);
        if (!updated.Success || !euro.Success) return null;

        if (!DateOnly.TryParseExact(updated.Groups["date"].Value, "dd.MM.yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)) return null;
        if (!TimeOnly.TryParseExact(updated.Groups["time"].Value, "HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time)) return null;
        if (!decimal.TryParse(euro.Groups["rate"].Value, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal rate)) return null;

        DateTime publishedLocal = date.ToDateTime(time, DateTimeKind.Unspecified);
        DateTime publishedUtc = TimeZoneInfo.ConvertTimeToUtc(publishedLocal, Tirane);
        return new BankOfAlbaniaRate(rate, date, publishedUtc);
    }

    internal static readonly TimeZoneInfo Tirane = TimeZoneInfo.FindSystemTimeZoneById("Europe/Tirane");
}
