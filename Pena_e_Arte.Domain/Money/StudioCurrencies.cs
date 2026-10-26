namespace Pena_e_Arte.Domain.Money;

/// <summary>
/// The currencies a studio may choose: ALL (primary and default) and EUR (secondary), the two POK
/// supports. <see cref="CurrencyCatalog"/> stays the full ISO 4217 list because formatting and
/// minor units still need it; this narrower list only gates what registration, solo sign-up and
/// the Studio Settings currency change accept. See docs/claude/architecture.md Decisions Log,
/// "Studio currency: ALL and EUR only (2026-10-26)".
/// </summary>
public static class StudioCurrencies
{
    public const string Primary = "ALL";
    public const string Secondary = "EUR";

    /// <summary>In picker order: primary first.</summary>
    public static readonly IReadOnlyList<string> Offered = [Primary, Secondary];

    /// <summary>True if <paramref name="code"/> is ALL or EUR (case-insensitive).</summary>
    public static bool IsOffered(string? code)
        => code is not null && Offered.Contains(code.ToUpperInvariant());

    /// <summary>
    /// The currency a form preselects for a studio in <paramref name="countryCode"/>: the country's own
    /// currency when it is one we offer (AL -> ALL; XK, ME, IT -> EUR), otherwise <see cref="Primary"/>.
    /// Never null, never throws.
    /// </summary>
    public static string DefaultFor(string countryCode)
    {
        string? countryCurrency = CountryCurrency.DefaultCurrencyFor(countryCode);
        return IsOffered(countryCurrency) ? countryCurrency! : Primary;
    }
}
