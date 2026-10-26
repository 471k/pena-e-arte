using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services.ExchangeRates;

namespace Pena_e_Arte.UnitTests.Helpers;

/// <summary>A real ReferenceAmountSnapshotter over a stubbed newest BoA EUR/ALL rate.</summary>
public static class ReferenceSnapshots
{
    public static readonly DateOnly RateDate = new(2026, 10, 23);

    /// <param name="rate">Lek per euro, or null for "no rate ever stored".</param>
    public static ReferenceAmountSnapshotter With(decimal? rate, bool stale = false)
    {
        IExchangeRateProvider rates = Substitute.For<IExchangeRateProvider>();
        rates.GetLatestAsync("EUR", "ALL", Arg.Any<CancellationToken>())
             .Returns(rate is null ? null : new ExchangeRateSnapshot(rate.Value, RateDate, "BoA-Fix", stale));
        return new ReferenceAmountSnapshotter(rates, NullLogger<ReferenceAmountSnapshotter>.Instance);
    }
}
