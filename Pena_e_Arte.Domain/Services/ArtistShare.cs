using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Money;

namespace Pena_e_Arte.Domain.Services;

/// <summary>Why an artist's share is what it is (A07a), so a screen can say it in words.</summary>
public enum ArtistShareBasis
{
    /// <summary>Booth rent: the artist keeps what the client paid; rent is charged separately.</summary>
    BoothRentKeepsAll,

    /// <summary>Commission: the rate's percentage of what the client paid.</summary>
    CommissionRate,

    /// <summary>Flat fee per session: what the client paid minus the studio's fee.</summary>
    FlatFeeDeducted,

    /// <summary>Salaried: nothing is earned per session.</summary>
    SalariedNoPerSessionShare,

    /// <summary>Commission with no rate set: the share is unknown.</summary>
    RateNotSet,

    /// <summary>A rate outside 0–100: treated as unknown, never clamped.</summary>
    RateOutOfRange,

    /// <summary>Flat fee per session with no fee set (or a negative one): the share is unknown.</summary>
    FeeNotSet,
}

/// <summary>
/// The arrangement that applies to one artist: each field is the artist's own value when they have one,
/// otherwise the studio's default (most specific wins, field by field).
/// </summary>
/// <param name="CommissionRate">Percent to the artist, 0–100 (60.00 = 60%). Null = not set.</param>
/// <param name="FlatFeePerSession">The studio's cut per session, in the studio currency. Null = not set.</param>
public sealed record ArtistPayTerms(
    ArtistPayArrangement Arrangement,
    decimal? CommissionRate,
    decimal? FlatFeePerSession);

/// <param name="ArtistAmount">The artist's share. 0 when <paramref name="IsKnown"/> is false: callers must not show it.</param>
/// <param name="IsKnown">False when the arrangement can't be computed (no rate, no fee, a rate out of range).</param>
/// <param name="Basis">Why the share is what it is.</param>
public sealed record ArtistShareResult(decimal ArtistAmount, bool IsKnown, ArtistShareBasis Basis);

/// <summary>
/// The one place an artist's share of a payment is worked out (DEC10, A07a), pure like
/// <see cref="CancellationOutcome"/> and <see cref="NoShowOutcome"/>. Gross basis only: the share is
/// computed on what the client paid (minus refunds), never on the amount after POK's fee, which isn't
/// stored. An unknown share is reported as unknown, never as a guessed rate or as the whole payment.
///
/// A payment is one session here: Payment has a unique index on AppointmentId, so "per session" and
/// "per payment" are the same and a flat fee can't be deducted twice for one appointment.
///
/// Amounts are rounded with <see cref="CurrencyCatalog.Round"/>, like every other amount in the studio
/// currency (the currency's minor units, midpoints away from zero).
/// </summary>
public static class ArtistShare
{
    /// <summary>Resolves the terms for one artist: their own value per field, else the studio's.</summary>
    public static ArtistPayTerms Resolve(
        ArtistPayArrangement? artistArrangement, decimal? artistCommissionRate, decimal? artistFlatFee,
        ArtistPayArrangement studioArrangement, decimal? studioCommissionRate, decimal? studioFlatFee) =>
        new(
            artistArrangement ?? studioArrangement,
            artistCommissionRate ?? studioCommissionRate,
            artistFlatFee ?? studioFlatFee);

    public static ArtistShareResult Compute(decimal retainedAmount, ArtistPayTerms terms, string currency) =>
        Compute(retainedAmount, terms.Arrangement, terms.CommissionRate, terms.FlatFeePerSession, currency);

    /// <param name="retainedAmount">What the client paid minus anything refunded.</param>
    /// <param name="arrangement">The resolved arrangement.</param>
    /// <param name="commissionRate">Percent to the artist, 0–100.</param>
    /// <param name="flatFeePerSession">The studio's cut per session.</param>
    /// <param name="currency">The payment's currency, for rounding.</param>
    public static ArtistShareResult Compute(
        decimal retainedAmount,
        ArtistPayArrangement arrangement,
        decimal? commissionRate,
        decimal? flatFeePerSession,
        string currency)
    {
        decimal retained = Math.Max(0m, retainedAmount);
        return arrangement switch
        {
            ArtistPayArrangement.BoothRent =>
                new ArtistShareResult(retained, true, ArtistShareBasis.BoothRentKeepsAll),

            ArtistPayArrangement.Salaried =>
                new ArtistShareResult(0m, true, ArtistShareBasis.SalariedNoPerSessionShare),

            ArtistPayArrangement.Commission => commissionRate switch
            {
                null => Unknown(ArtistShareBasis.RateNotSet),
                < 0m or > 100m => Unknown(ArtistShareBasis.RateOutOfRange),
                decimal rate => new ArtistShareResult(
                    CurrencyCatalog.Round(retained * rate / 100m, currency), true, ArtistShareBasis.CommissionRate),
            },

            ArtistPayArrangement.FlatFeePerSession => flatFeePerSession switch
            {
                null or < 0m => Unknown(ArtistShareBasis.FeeNotSet),
                decimal fee => new ArtistShareResult(
                    CurrencyCatalog.Round(Math.Max(0m, retained - fee), currency), true, ArtistShareBasis.FlatFeeDeducted),
            },

            _ => throw new ArgumentOutOfRangeException(nameof(arrangement), arrangement, null),
        };
    }

    private static ArtistShareResult Unknown(ArtistShareBasis basis) => new(0m, false, basis);
}
