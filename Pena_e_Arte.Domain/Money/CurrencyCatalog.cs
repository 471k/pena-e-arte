using System.Collections.Frozen;

namespace Pena_e_Arte.Domain.Money;

/// <summary>
/// Active, tradable ISO 4217 currency codes and their minor units ("decimal places").
/// This is a currency-&gt;minor-unit table, which .NET does not expose per currency (see
/// <see cref="CountryCurrency"/> for country-&gt;currency, which .NET does expose via
/// <c>RegionInfo</c>).
///
/// Source: ISO 4217 "List One" (active currencies), as published by SIX (the ISO 4217
/// maintenance agency). Transcribed 2026-09-27. Deliberately excludes non-tradable / fund
/// codes that ISO 4217 also lists: precious metals (XAU, XAG, XPD, XPT), the IMF's XDR, the
/// "no currency" code XXX, testing codes (XTS), bond-market units (XBA-XBD), the ADB unit
/// (XUA), the Sucre (XSU), and inflation-indexed / notional fund codes (CLF, UYW, BOV, CHE,
/// CHW, COU, MXV, USN). None of these are ever a studio's operating currency.
/// </summary>
public static class CurrencyCatalog
{
    /// <summary>ISO 4217 codes with 0 decimal places (whole-unit currencies).</summary>
    private static readonly FrozenSet<string> ZeroDecimalCodes = new[]
    {
        "BIF", "CLP", "DJF", "GNF", "ISK", "JPY", "KMF", "KRW", "PYG", "RWF", "UGX", "VND",
        "VUV", "XAF", "XOF", "XPF",
    }.ToFrozenSet();

    /// <summary>ISO 4217 codes with 3 decimal places.</summary>
    private static readonly FrozenSet<string> ThreeDecimalCodes = new[]
    {
        "BHD", "IQD", "JOD", "KWD", "LYD", "OMR", "TND",
    }.ToFrozenSet();

    /// <summary>
    /// Every active, tradable ISO 4217 code this platform recognises, mapped to its minor
    /// unit count. Everything not listed as zero- or three-decimal above defaults to 2.
    /// </summary>
    private static readonly FrozenDictionary<string, int> MinorUnitsByCode = BuildTable();

    private static FrozenDictionary<string, int> BuildTable()
    {
        string[] codes =
        [
            "AED", "AFN", "ALL", "AMD", "ANG", "AOA", "ARS", "AUD", "AWG", "AZN", "BAM", "BBD",
            "BDT", "BGN", "BHD", "BIF", "BMD", "BND", "BOB", "BRL", "BSD", "BTN", "BWP", "BYN",
            "BZD", "CAD", "CDF", "CHF", "CLP", "CNY", "COP", "CRC", "CUP", "CVE", "CZK", "DJF",
            "DKK", "DOP", "DZD", "EGP", "ERN", "ETB", "EUR", "FJD", "FKP", "GBP", "GEL", "GHS",
            "GIP", "GMD", "GNF", "GTQ", "GYD", "HKD", "HNL", "HTG", "HUF", "IDR", "ILS", "INR",
            "IQD", "IRR", "ISK", "JMD", "JOD", "JPY", "KES", "KGS", "KHR", "KMF", "KPW", "KRW",
            "KWD", "KYD", "KZT", "LAK", "LBP", "LKR", "LRD", "LSL", "LYD", "MAD", "MDL", "MGA",
            "MKD", "MMK", "MNT", "MOP", "MRU", "MUR", "MVR", "MWK", "MXN", "MYR", "MZN", "NAD",
            "NGN", "NIO", "NOK", "NPR", "NZD", "OMR", "PAB", "PEN", "PGK", "PHP", "PKR", "PLN",
            "PYG", "QAR", "RON", "RSD", "RUB", "RWF", "SAR", "SBD", "SCR", "SDG", "SEK", "SGD",
            "SHP", "SLE", "SOS", "SRD", "SSP", "STN", "SVC", "SYP", "SZL", "THB", "TJS", "TMT",
            "TND", "TOP", "TRY", "TTD", "TWD", "TZS", "UAH", "UGX", "USD", "UYU", "UZS", "VES",
            "VND", "VUV", "WST", "XAF", "XCD", "XOF", "XPF", "YER", "ZAR", "ZMW", "ZWG",
        ];

        Dictionary<string, int> table = new(StringComparer.Ordinal);
        foreach (string code in codes)
        {
            table[code] = ZeroDecimalCodes.Contains(code) ? 0
                : ThreeDecimalCodes.Contains(code) ? 3
                : 2;
        }

        return table.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Every active currency code this platform recognises, upper-case.</summary>
    public static IReadOnlyCollection<string> AllCodes { get; } = MinorUnitsByCode.Keys;

    /// <summary>True if <paramref name="code"/> is a known, active ISO 4217 code (case-insensitive).</summary>
    public static bool IsSupported(string code)
        => !string.IsNullOrWhiteSpace(code) && MinorUnitsByCode.ContainsKey(code.ToUpperInvariant());

    /// <summary>The number of decimal places (minor units) this currency is quoted in.</summary>
    public static int MinorUnits(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || !MinorUnitsByCode.TryGetValue(code.ToUpperInvariant(), out int units))
        {
            throw new ArgumentException($"Unknown or unsupported currency code: '{code}'.", nameof(code));
        }

        return units;
    }

    /// <summary>
    /// Rounds <paramref name="amount"/> to this currency's minor unit, away from zero at the
    /// midpoint (never banker's rounding — money never rounds toward an even digit).
    /// </summary>
    public static decimal Round(decimal amount, string code)
        => Math.Round(amount, MinorUnits(code), MidpointRounding.AwayFromZero);

    /// <summary>True if <paramref name="amount"/> has no more decimal places than this currency allows.</summary>
    public static bool HasAtMostMinorUnits(decimal amount, string code)
        => amount == Round(amount, code);
}
