using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.Domain.Services;

/// <summary>
/// Resolves the deposit amount for a booking from the studio's active deposit rule.
/// Fixed rules return their amount directly; percent rules apply to the estimated
/// session price (artist hourly rate × duration). A percent rule without an artist
/// rate has no base to compute from and yields no deposit.
/// </summary>
public static class DepositCalculator
{
    public static decimal Calculate(DepositRule? rule, decimal? artistHourlyRate, int durationMinutes, string currency)
    {
        if (rule is null) return 0m;

        if (rule.AmountFixed is decimal fixedAmount)
            return fixedAmount;

        if (rule.AmountPercent is decimal percent && artistHourlyRate is > 0m)
        {
            decimal estimatedSessionPrice = artistHourlyRate.Value * durationMinutes / 60m;
            return CurrencyCatalog.Round(estimatedSessionPrice * percent / 100m, currency);
        }

        return 0m;
    }
}
