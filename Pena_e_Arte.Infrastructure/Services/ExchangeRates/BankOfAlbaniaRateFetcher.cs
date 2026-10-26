using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.Infrastructure.Services.ExchangeRates;

public enum RateFetchOutcome
{
    Stored,
    AlreadyStored,
    Rejected,
    Unreadable,
}

/// <summary>
/// Downloads the Bank of Albania official rate page, reads the EUR "Fix" rate and stores it once per
/// rate date. Validates before saving and saves nothing when a check fails: the lek figure is shown to
/// clients, so a wrong number is worse than a missing one (the app then shows EUR only, or the last good
/// rate labelled with its date). No third-party rate service is used (spec §6 Phase 0).
/// </summary>
public class BankOfAlbaniaRateFetcher(
    IHttpClientFactory httpClientFactory,
    IAppDbContext db,
    TimeProvider time,
    ILogger<BankOfAlbaniaRateFetcher> logger)
{
    public const string HttpClientName = "BankOfAlbania";
    public const string PageUrl = "https://www.bankofalbania.org/Markets/Official_exchange_rate/";

    internal const decimal MinPlausibleRate = 50m;
    internal const decimal MaxPlausibleRate = 200m;
    internal const decimal MaxDayToDayChange = 0.05m;

    public async Task<RateFetchOutcome> FetchAndStoreAsync(CancellationToken ct)
    {
        HttpClient client = httpClientFactory.CreateClient(HttpClientName);
        string html = await client.GetStringAsync(PageUrl, ct);

        BankOfAlbaniaRate? parsed = BankOfAlbaniaRateParser.Parse(html);
        if (parsed is null)
        {
            logger.LogError("BoA EUR/ALL rate not found on the official rate page — the page layout may have changed");
            return RateFetchOutcome.Unreadable;
        }

        return await StoreAsync(parsed, ct);
    }

    /// <summary>Validates and stores an already-parsed rate. Separate from the download so tests can
    /// drive it without HTTP.</summary>
    internal async Task<RateFetchOutcome> StoreAsync(BankOfAlbaniaRate parsed, CancellationToken ct)
    {
        ExchangeRate? previous = await db.ExchangeRates
            .Where(r => r.BaseCurrency == ReferenceAmount.BaseCurrency
                        && r.QuoteCurrency == ReferenceAmount.ReferenceCurrency
                        && r.Source == ReferenceAmount.BankOfAlbaniaFixSource)
            .OrderByDescending(r => r.RateDate)
            .FirstOrDefaultAsync(ct);

        if (previous is not null && previous.RateDate == parsed.RateDate)
            return RateFetchOutcome.AlreadyStored;

        DateTime nowUtc = time.GetUtcNow().UtcDateTime;
        DateOnly todayTirane = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, BankOfAlbaniaRateParser.Tirane));

        string? rejection = Validate(parsed, previous, todayTirane);
        if (rejection is not null)
        {
            logger.LogError(
                "BoA EUR/ALL rate rejected, nothing stored: {Reason} (published {RateDate}, rate {Rate})",
                rejection, parsed.RateDate, parsed.Rate);
            return RateFetchOutcome.Rejected;
        }

        db.ExchangeRates.Add(ExchangeRate.Create(
            ReferenceAmount.BaseCurrency, ReferenceAmount.ReferenceCurrency, parsed.RateDate, parsed.Rate,
            ReferenceAmount.BankOfAlbaniaFixSource, parsed.PublishedAtUtc, nowUtc));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("BoA EUR/ALL rate stored for {RateDate}: {Rate}", parsed.RateDate, parsed.Rate);
        return RateFetchOutcome.Stored;
    }

    /// <summary>Null when the rate may be stored; otherwise why not.</summary>
    internal static string? Validate(BankOfAlbaniaRate candidate, ExchangeRate? previous, DateOnly todayTirane)
    {
        if (candidate.Rate < MinPlausibleRate || candidate.Rate > MaxPlausibleRate)
            return $"rate outside {MinPlausibleRate}-{MaxPlausibleRate}";
        if (candidate.RateDate > todayTirane)
            return "rate date is in the future";
        if (previous is null)
            return null;
        if (candidate.RateDate < previous.RateDate)
            return "rate date is older than the newest stored rate";
        if (Math.Abs(candidate.Rate - previous.Rate) > previous.Rate * MaxDayToDayChange)
            return "rate moved more than 5% from the newest stored rate";
        return null;
    }
}
