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

    /// <summary>
    /// <see cref="Format"/> plus the lek reference in brackets for Albanian EUR studios (spec D7):
    /// "50.00 EUR (4,537 ALL)" once frozen at payment, "50.00 EUR (≈ 4,537 ALL)" before payment. Whole
    /// lek. Exactly <see cref="Format"/> when there is no reference, so every other studio's text is
    /// unchanged.
    /// </summary>
    public static string FormatWithReference(
        decimal amount, string currency, decimal? referenceAmount, string? referenceCurrency, bool approximate)
    {
        string main = Format(amount, currency);
        if (referenceAmount is null || string.IsNullOrWhiteSpace(referenceCurrency)) return main;

        string lek = referenceAmount.Value.ToString("N0", CultureInfo.InvariantCulture);
        return $"{main} ({(approximate ? "≈ " : "")}{lek} {referenceCurrency.ToUpperInvariant()})";
    }

    /// <summary>"1 EUR = 90.74 ALL, Bank of Albania official rate of 23 Oct 2026" — the rate used for a
    /// frozen lek reference, for receipts and payment messages (spec D7).</summary>
    public static string BankOfAlbaniaRateLine(decimal rate, DateOnly rateDate) =>
        string.Create(CultureInfo.InvariantCulture,
            $"1 EUR = {rate:0.00####} ALL, Bank of Albania official rate of {rateDate:d MMM yyyy}");
}
