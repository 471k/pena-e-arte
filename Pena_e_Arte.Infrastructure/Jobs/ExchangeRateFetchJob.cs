using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Money;
using Pena_e_Arte.Infrastructure.Services.ExchangeRates;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Fetches the Bank of Albania EUR/ALL official rate. BoA sets it from the 11:30-12:00 interbank window
/// and publishes on business days only, so RunAsync is scheduled on weekdays at 12:40, 14:00 and 17:00
/// Europe/Tirane: a late publication is still picked up, and a repeat run for a stored date is a no-op.
/// A thrown error reaches HangfireJobFailureLogFilter like any other job. CheckStaleAsync runs daily and
/// logs an Error when the newest rate is more than a week old (spec Q2: lek keeps showing from the last
/// rate, labelled with its date; checkout is not blocked).
/// </summary>
public class ExchangeRateFetchJob(
    BankOfAlbaniaRateFetcher fetcher,
    IExchangeRateProvider rates,
    ILogger<ExchangeRateFetchJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        RateFetchOutcome outcome = await fetcher.FetchAndStoreAsync(ct);
        logger.LogInformation("ExchangeRateFetchJob finished: {Outcome}", outcome);
    }

    public async Task CheckStaleAsync(CancellationToken ct = default)
    {
        ExchangeRateSnapshot? latest = await rates.GetLatestAsync(
            ReferenceAmount.BaseCurrency, ReferenceAmount.ReferenceCurrency, ct);

        // Warning, not Error: right after the first deploy this check can run before the first weekday fetch.
        if (latest is null)
            logger.LogWarning("BoA EUR/ALL rate is missing: none has ever been stored");
        else if (latest.IsStale)
            logger.LogError("BoA EUR/ALL rate is stale: newest {RateDate}", latest.RateDate);
    }
}
