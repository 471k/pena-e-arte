using System.Globalization;

namespace Pena_e_Arte.Domain.Money;

/// <summary>
/// Locale-neutral, unambiguous money formatting for text that may be read in any locale
/// (emails, PDFs, calendar files): "{amount} {ISO CODE}", e.g. "5,000.00 ALL", "50.00 EUR",
/// "3,000 JPY", "12.345 KWD". The symbol form ("€50") is a frontend-only concern
/// (<c>formatCurrency</c> in <c>frontend/src/shared/utils/formatCurrency.ts</c>).
/// </summary>
public static class MoneyText
{
    public static string Format(decimal amount, string currency)
    {
        int minorUnits = CurrencyCatalog.MinorUnits(currency);
        string formatted = amount.ToString("N" + minorUnits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return $"{formatted} {currency.ToUpperInvariant()}";
    }
}
