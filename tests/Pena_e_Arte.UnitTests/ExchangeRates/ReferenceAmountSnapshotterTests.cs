using FluentAssertions;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ExchangeRates;

public class ReferenceAmountSnapshotterTests
{
    [Fact]
    public async Task FreezeAsync_AlbanianEuroStudio_SetsAllFiveFields()
    {
        Payment payment = new() { Amount = 50m, Currency = "EUR" };

        await ReferenceSnapshots.With(90.74m).FreezeAsync(payment, "AL", "EUR", payment.Amount, default);

        payment.ReferenceCurrency.Should().Be("ALL");
        payment.ReferenceRate.Should().Be(90.74m);
        payment.ReferenceRateDate.Should().Be(ReferenceSnapshots.RateDate);
        payment.ReferenceRateSource.Should().Be("BoA-Fix");
        payment.ReferenceAmount.Should().Be(4537m);
    }

    [Theory]
    [InlineData("AL", "ALL")]
    [InlineData("XK", "EUR")]
    [InlineData("AL", "USD")]
    public async Task FreezeAsync_StudioThatIsNotAlbanianEuro_LeavesNoReference(string country, string currency)
    {
        GiftCard card = new() { InitialBalance = 50m, Currency = currency };

        await ReferenceSnapshots.With(90.74m).FreezeAsync(card, country, currency, card.InitialBalance, default);

        card.ReferenceAmount.Should().BeNull();
        card.ReferenceRate.Should().BeNull();
        card.ReferenceCurrency.Should().BeNull();
        card.ReferenceRateDate.Should().BeNull();
        card.ReferenceRateSource.Should().BeNull();
    }

    [Fact]
    public async Task FreezeAsync_NoRateEverStored_LeavesNoReferenceAndDoesNotThrow()
    {
        PackagePurchase purchase = new() { Amount = 120m, Currency = "EUR" };

        Func<Task> act = () => ReferenceSnapshots.With(null).FreezeAsync(purchase, "AL", "EUR", purchase.Amount, default);

        await act.Should().NotThrowAsync();
        purchase.ReferenceAmount.Should().BeNull();
        purchase.ReferenceRate.Should().BeNull();
    }

    [Fact]
    public async Task FreezeAsync_StaleRate_IsStillFrozenWithItsOwnDate()
    {
        Payment payment = new() { Amount = 10m, Currency = "EUR" };

        await ReferenceSnapshots.With(90.74m, stale: true).FreezeAsync(payment, "AL", "EUR", payment.Amount, default);

        payment.ReferenceAmount.Should().Be(907m);
        payment.ReferenceRateDate.Should().Be(ReferenceSnapshots.RateDate);
    }

    [Fact]
    public async Task FreezeAsync_FreshOrderOnARecordThatHadAReference_ReplacesIt()
    {
        Payment payment = new()
        {
            Amount = 50m,
            Currency = "EUR",
            ReferenceCurrency = "ALL",
            ReferenceRate = 99m,
            ReferenceRateDate = new DateOnly(2026, 1, 2),
            ReferenceRateSource = "BoA-Fix",
            ReferenceAmount = 4950m,
        };

        await ReferenceSnapshots.With(90.74m).FreezeAsync(payment, "AL", "EUR", payment.Amount, default);

        payment.ReferenceRate.Should().Be(90.74m);
        payment.ReferenceAmount.Should().Be(4537m);
    }
}
