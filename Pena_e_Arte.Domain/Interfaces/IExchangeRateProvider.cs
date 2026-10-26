namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// Reads the newest stored exchange rate (see ExchangeRate). Display and record-keeping only: the
/// lek figure it feeds is never sent to the card provider.
/// </summary>
public interface IExchangeRateProvider
{
    /// <summary>The newest stored rate for the pair, or null if none has ever been stored.</summary>
    Task<ExchangeRateSnapshot?> GetLatestAsync(string baseCurrency, string quoteCurrency, CancellationToken ct);
}

/// <param name="Rate">Quote-currency units per one base-currency unit.</param>
/// <param name="RateDate">The date the source states for the rate.</param>
/// <param name="Source">For example "BoA-Fix".</param>
/// <param name="IsStale">True when <paramref name="RateDate"/> is more than
/// <see cref="ExchangeRateSnapshot.StaleAfterDays"/> calendar days before today in Europe/Tirane.</param>
public record ExchangeRateSnapshot(decimal Rate, DateOnly RateDate, string Source, bool IsStale)
{
    /// <summary>BoA publishes on business days only, so a week covers long weekends and holidays.</summary>
    public const int StaleAfterDays = 7;
}
