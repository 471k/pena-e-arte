using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.Infrastructure.Services.ExchangeRates;

/// <inheritdoc />
public class ReferenceAmountSnapshotter(IExchangeRateProvider rates, ILogger<ReferenceAmountSnapshotter> logger)
    : IReferenceAmountSnapshotter
{
    public async Task FreezeAsync(
        IHasReferenceAmount record, string studioCountryCode, string currency, decimal amount, CancellationToken ct)
    {
        if (!ReferenceAmount.AppliesTo(studioCountryCode, currency))
        {
            ReferenceAmount.Clear(record);
            return;
        }

        ExchangeRateSnapshot? rate = await rates.GetLatestAsync(
            ReferenceAmount.BaseCurrency, ReferenceAmount.ReferenceCurrency, ct);

        if (rate is null)
        {
            ReferenceAmount.Clear(record);
            logger.LogWarning(
                "No BoA EUR/ALL rate stored; {RecordType} saved without a lek reference", record.GetType().Name);
            return;
        }

        // A stale rate is still frozen, with its own date, so the receipt states exactly which rate was
        // used (spec Q2: lek keeps showing from the newest rate; checkout is not blocked).
        ReferenceAmount.Freeze(record, amount, rate.Rate, rate.RateDate, rate.Source);
    }
}
