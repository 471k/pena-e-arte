namespace Pena_e_Arte.Contracts.Responses;

/// <summary>The newest Bank of Albania EUR/ALL official rate (lek per euro).</summary>
/// <param name="Rate">Lek per one euro, e.g. 90.74.</param>
/// <param name="RateDate">The date BoA states for the rate, "yyyy-MM-dd".</param>
/// <param name="Source">"BoA-Fix".</param>
/// <param name="IsStale">True when the rate is more than 7 calendar days old (Europe/Tirane).</param>
public record ExchangeRateResponse(decimal Rate, string RateDate, string Source, bool IsStale);
