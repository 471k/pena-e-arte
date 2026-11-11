using FluentAssertions;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.UnitTests.Services;

/// <summary>A07a phase 3: every branch of ArtistShare.Compute and the artist-then-studio resolution.</summary>
public class ArtistShareTests
{
    private static ArtistShareResult Compute(
        decimal retained, ArtistPayArrangement arrangement, decimal? rate = null, decimal? fee = null, string currency = "EUR") =>
        ArtistShare.Compute(retained, arrangement, rate, fee, currency);

    [Fact]
    public void BoothRent_ArtistKeepsTheWholePayment() =>
        Compute(10_000m, ArtistPayArrangement.BoothRent, rate: 60m)
            .Should().Be(new ArtistShareResult(10_000m, true, ArtistShareBasis.BoothRentKeepsAll));

    [Fact]
    public void Commission_WithARate_IsThatPercentageOfThePayment() =>
        Compute(10_000m, ArtistPayArrangement.Commission, rate: 60m)
            .Should().Be(new ArtistShareResult(6_000m, true, ArtistShareBasis.CommissionRate));

    [Fact]
    public void Commission_WithNoRate_IsUnknown_NeverTheGrossOrAGuess() =>
        Compute(10_000m, ArtistPayArrangement.Commission, rate: null)
            .Should().Be(new ArtistShareResult(0m, false, ArtistShareBasis.RateNotSet));

    [Fact]
    public void Commission_ZeroPercent_IsKnownZero_UnlikeNoRate() =>
        Compute(10_000m, ArtistPayArrangement.Commission, rate: 0m)
            .Should().Be(new ArtistShareResult(0m, true, ArtistShareBasis.CommissionRate));

    [Fact]
    public void Commission_HundredPercent_IsThePayment() =>
        Compute(10_000m, ArtistPayArrangement.Commission, rate: 100m).ArtistAmount.Should().Be(10_000m);

    [Theory]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(60_000)]
    public void Commission_RateOutsideZeroToHundred_IsUnknown_NotClamped(decimal rate) =>
        Compute(10_000m, ArtistPayArrangement.Commission, rate: rate)
            .Should().Be(new ArtistShareResult(0m, false, ArtistShareBasis.RateOutOfRange));

    [Fact]
    public void Commission_HalfPercentRate_IsSupported() =>
        Compute(1_000m, ArtistPayArrangement.Commission, rate: 62.5m).ArtistAmount.Should().Be(625m);

    [Fact]
    public void Commission_OnAPartiallyRefundedPayment_UsesWhatWasKept() =>
        // 10,000 paid, 4,000 refunded: 60% of the 6,000 kept.
        Compute(6_000m, ArtistPayArrangement.Commission, rate: 60m).ArtistAmount.Should().Be(3_600m);

    [Fact]
    public void Commission_RoundsToTheCurrencysMinorUnits_AwayFromZero()
    {
        Compute(10.05m, ArtistPayArrangement.Commission, rate: 50m, currency: "EUR").ArtistAmount.Should().Be(5.03m);
        Compute(1_001m, ArtistPayArrangement.Commission, rate: 50m, currency: "JPY").ArtistAmount.Should().Be(501m);
    }

    [Fact]
    public void FlatFee_IsThePaymentMinusTheStudiosFee() =>
        Compute(10_000m, ArtistPayArrangement.FlatFeePerSession, fee: 2_500m)
            .Should().Be(new ArtistShareResult(7_500m, true, ArtistShareBasis.FlatFeeDeducted));

    [Fact]
    public void FlatFee_LargerThanThePayment_IsZeroNotNegative() =>
        Compute(1_000m, ArtistPayArrangement.FlatFeePerSession, fee: 2_500m)
            .Should().Be(new ArtistShareResult(0m, true, ArtistShareBasis.FlatFeeDeducted));

    [Theory]
    [InlineData(null)]
    [InlineData(-5)]
    public void FlatFee_NotSetOrNegative_IsUnknown(int? fee) =>
        Compute(10_000m, ArtistPayArrangement.FlatFeePerSession, fee: fee)
            .Should().Be(new ArtistShareResult(0m, false, ArtistShareBasis.FeeNotSet));

    [Fact]
    public void Salaried_IsAKnownZero() =>
        Compute(10_000m, ArtistPayArrangement.Salaried, rate: 60m)
            .Should().Be(new ArtistShareResult(0m, true, ArtistShareBasis.SalariedNoPerSessionShare));

    [Fact]
    public void Resolve_TakesTheArtistsOwnValue_FieldByField_ElseTheStudios()
    {
        ArtistShare.Resolve(null, null, null, ArtistPayArrangement.Commission, 50m, 100m)
            .Should().Be(new ArtistPayTerms(ArtistPayArrangement.Commission, 50m, 100m));
        ArtistShare.Resolve(ArtistPayArrangement.FlatFeePerSession, 70m, 300m, ArtistPayArrangement.Commission, 50m, 100m)
            .Should().Be(new ArtistPayTerms(ArtistPayArrangement.FlatFeePerSession, 70m, 300m));
        ArtistShare.Resolve(null, 70m, null, ArtistPayArrangement.Commission, null, null)
            .Should().Be(new ArtistPayTerms(ArtistPayArrangement.Commission, 70m, null), "an artist's own rate counts under the studio's arrangement");
    }
}
