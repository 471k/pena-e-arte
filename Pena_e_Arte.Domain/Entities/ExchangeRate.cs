namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// One published central-bank exchange rate: how many <see cref="QuoteCurrency"/> units one
/// <see cref="BaseCurrency"/> unit buys on <see cref="RateDate"/>. Today only the Bank of Albania's
/// official "Fix" rate for EUR -> ALL, fetched on business days by ExchangeRateFetchJob. Global,
/// not a TenantEntity: a public rate belongs to no studio, so there is no query filter and nothing
/// to bypass. Used only to show the lek amount next to euro amounts clients of Albanian EUR studios
/// see; it never reaches the card provider. See docs/claude/architecture.md Decisions Log,
/// "EUR studios in Albania show lek (2026-10-26)".
/// </summary>
public class ExchangeRate
{
    private ExchangeRate() { }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string BaseCurrency { get; private set; } = string.Empty;
    public string QuoteCurrency { get; private set; } = string.Empty;
    /// <summary>The date the source states for this rate (Europe/Tirane), not the fetch date.</summary>
    public DateOnly RateDate { get; private set; }
    /// <summary>Quote-currency units per one base-currency unit, e.g. 90.740000 lek per euro.</summary>
    public decimal Rate { get; private set; }
    /// <summary>Where the rate comes from, e.g. "BoA-Fix" (Bank of Albania official rate).</summary>
    public string Source { get; private set; } = string.Empty;
    /// <summary>The publication time the source states, converted from Europe/Tirane to UTC.</summary>
    public DateTime PublishedAtUtc { get; private set; }
    public DateTime FetchedAtUtc { get; private set; }

    public static ExchangeRate Create(
        string baseCurrency, string quoteCurrency, DateOnly rateDate, decimal rate,
        string source, DateTime publishedAtUtc, DateTime fetchedAtUtc) =>
        new()
        {
            BaseCurrency = baseCurrency,
            QuoteCurrency = quoteCurrency,
            RateDate = rateDate,
            Rate = rate,
            Source = source,
            PublishedAtUtc = publishedAtUtc,
            FetchedAtUtc = fetchedAtUtc,
        };
}
