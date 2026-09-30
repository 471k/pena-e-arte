using System.Globalization;

namespace Pena_e_Arte.Domain.Money;

/// <summary>
/// Country (ISO 3166-1 alpha-2) -&gt; default currency, sourced from .NET's own <see cref="RegionInfo"/>
/// (ICU region data), never a hand-kept table. This only ever picks the *default* a form
/// preselects; the owner may always choose a different <see cref="CurrencyCatalog"/> currency.
/// </summary>
public static class CountryCurrency
{
    /// <summary>
    /// The ISO 4217 currency a country uses by default, or null if the country code is
    /// unrecognised by <see cref="RegionInfo"/> or maps to a code <see cref="CurrencyCatalog"/>
    /// does not list. Never throws.
    /// </summary>
    public static string? DefaultCurrencyFor(string countryCode)
    {
        if (string.IsNullOrWhiteSpace(countryCode))
        {
            return null;
        }

        try
        {
            RegionInfo region = new(countryCode);
            string currency = region.ISOCurrencySymbol.ToUpperInvariant();
            return CurrencyCatalog.IsSupported(currency) ? currency : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>True if <paramref name="code"/> is a two-letter code <see cref="RegionInfo"/> recognises.</summary>
    public static bool IsKnownCountry(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 2 || !code.All(char.IsAsciiLetterUpper))
        {
            return false;
        }

        try
        {
            _ = new RegionInfo(code);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
