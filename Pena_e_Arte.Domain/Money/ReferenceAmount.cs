using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Domain.Money;

/// <summary>
/// The lek amount shown next to a euro amount for clients of Albanian studios that price in EUR
/// (docs/specs/spec-eur-with-lek-display-2026-10-26.md, D4 and D9). Display and record-keeping
/// only: the card is still charged in EUR and nothing here reaches the card provider.
/// </summary>
public static class ReferenceAmount
{
    public const string ReferenceCurrency = "ALL";
    public const string BaseCurrency = "EUR";
    public const string BankOfAlbaniaFixSource = "BoA-Fix";

    /// <summary>
    /// EUR amount x lek-per-euro rate, computed in decimal with no intermediate rounding, then
    /// rounded once to whole lek, midpoint away from zero (the same rule as CurrencyCatalog.Round).
    /// Whole lek because qindarka aren't used in practice and the app already shows ALL with no
    /// decimals. Convert each displayed amount itself; never sum converted lines.
    /// </summary>
    public static decimal ToWholeLek(decimal eurAmount, decimal rate)
        => Math.Round(eurAmount * rate, 0, MidpointRounding.AwayFromZero);

    /// <summary>True for a studio in Albania that prices in euros (case-insensitive). Every other
    /// studio sees no change at all (D9).</summary>
    public static bool AppliesTo(Studio studio)
        => AppliesTo(studio.CountryCode, studio.Currency);

    /// <inheritdoc cref="AppliesTo(Studio)"/>
    public static bool AppliesTo(string? countryCode, string? currency)
        => string.Equals(countryCode, "AL", StringComparison.OrdinalIgnoreCase)
           && string.Equals(currency, BaseCurrency, StringComparison.OrdinalIgnoreCase);

    /// <summary>Freezes the lek reference for <paramref name="eurAmount"/> at <paramref name="rate"/> onto
    /// <paramref name="record"/> (spec D5). Overwrites any earlier reference on the same record, which
    /// only happens when a fresh provider order replaces an old one.</summary>
    public static void Freeze(IHasReferenceAmount record, decimal eurAmount, decimal rate, DateOnly rateDate, string source)
    {
        record.ReferenceCurrency = ReferenceCurrency;
        record.ReferenceRate = rate;
        record.ReferenceRateDate = rateDate;
        record.ReferenceRateSource = source;
        record.ReferenceAmount = ToWholeLek(eurAmount, rate);
    }

    /// <summary>Removes any lek reference: the record shows EUR only, as before this feature.</summary>
    public static void Clear(IHasReferenceAmount record)
    {
        record.ReferenceCurrency = null;
        record.ReferenceRate = null;
        record.ReferenceRateDate = null;
        record.ReferenceRateSource = null;
        record.ReferenceAmount = null;
    }
}
