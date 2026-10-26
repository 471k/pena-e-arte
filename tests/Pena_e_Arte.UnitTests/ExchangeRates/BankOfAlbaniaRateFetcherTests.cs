using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Infrastructure.Services.ExchangeRates;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ExchangeRates;

public class BankOfAlbaniaRateFetcherTests
{
    private static readonly DateOnly Friday = new(2026, 10, 23);
    // Monday 26 Oct 2026, 11:00 UTC = 12:00 in Tirane.
    private static readonly FixedTimeProvider Monday = new(new DateTimeOffset(2026, 10, 26, 11, 0, 0, TimeSpan.Zero));

    private readonly FakeDbContext _db = FakeDbContext.Create();

    private BankOfAlbaniaRateFetcher CreateSut() =>
        new(Substitute.For<IHttpClientFactory>(), _db, Monday, NullLogger<BankOfAlbaniaRateFetcher>.Instance);

    private static BankOfAlbaniaRate Rate(decimal rate, DateOnly date) =>
        new(rate, date, date.ToDateTime(new TimeOnly(10, 16)).ToUniversalTime());

    private static ExchangeRate Stored(decimal rate, DateOnly date) =>
        ExchangeRate.Create("EUR", "ALL", date, rate, "BoA-Fix", DateTime.UtcNow, DateTime.UtcNow);

    // ── Validation ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(49.99)]
    [InlineData(200.01)]
    [InlineData(0)]
    public void Validate_RateOutsidePlausibleBounds_Rejects(decimal rate) =>
        BankOfAlbaniaRateFetcher.Validate(Rate(rate, Friday), null, Friday).Should().NotBeNull();

    [Theory]
    [InlineData(50)]
    [InlineData(90.74)]
    [InlineData(200)]
    public void Validate_PlausibleFirstRate_Accepts(decimal rate) =>
        BankOfAlbaniaRateFetcher.Validate(Rate(rate, Friday), null, Friday).Should().BeNull();

    [Fact]
    public void Validate_DateInTheFuture_Rejects() =>
        BankOfAlbaniaRateFetcher.Validate(Rate(90.74m, Friday.AddDays(1)), null, Friday).Should().NotBeNull();

    [Fact]
    public void Validate_DateOlderThanNewestStored_Rejects() =>
        BankOfAlbaniaRateFetcher.Validate(Rate(90.74m, Friday.AddDays(-1)), Stored(90.70m, Friday), Friday)
            .Should().NotBeNull();

    [Theory]
    [InlineData(95.27)] // +4.99 %
    [InlineData(86.21)] // -4.99 %
    public void Validate_WithinFivePercentOfPrevious_Accepts(decimal rate) =>
        BankOfAlbaniaRateFetcher.Validate(Rate(rate, Friday), Stored(90.74m, Friday.AddDays(-1)), Friday)
            .Should().BeNull();

    [Theory]
    [InlineData(95.29)] // +5.01 %
    [InlineData(86.19)] // -5.01 %
    public void Validate_MoreThanFivePercentFromPrevious_Rejects(decimal rate) =>
        BankOfAlbaniaRateFetcher.Validate(Rate(rate, Friday), Stored(90.74m, Friday.AddDays(-1)), Friday)
            .Should().NotBeNull();

    // ── Storing ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task StoreAsync_FirstRate_StoresOneRow()
    {
        RateFetchOutcome outcome = await CreateSut().StoreAsync(Rate(90.74m, Friday), default);

        outcome.Should().Be(RateFetchOutcome.Stored);
        ExchangeRate row = _db.ExchangeRates.Single();
        row.Rate.Should().Be(90.74m);
        row.RateDate.Should().Be(Friday);
        row.BaseCurrency.Should().Be("EUR");
        row.QuoteCurrency.Should().Be("ALL");
        row.Source.Should().Be("BoA-Fix");
        row.FetchedAtUtc.Should().Be(Monday.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task StoreAsync_SameDateTwice_IsIdempotent()
    {
        BankOfAlbaniaRateFetcher sut = CreateSut();
        await sut.StoreAsync(Rate(90.74m, Friday), default);

        RateFetchOutcome second = await sut.StoreAsync(Rate(90.74m, Friday), default);

        second.Should().Be(RateFetchOutcome.AlreadyStored);
        _db.ExchangeRates.Should().HaveCount(1);
    }

    [Fact]
    public async Task StoreAsync_RejectedRate_StoresNothing()
    {
        RateFetchOutcome outcome = await CreateSut().StoreAsync(Rate(9.07m, Friday), default);

        outcome.Should().Be(RateFetchOutcome.Rejected);
        _db.ExchangeRates.Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_NextBusinessDay_AddsASecondRow()
    {
        BankOfAlbaniaRateFetcher sut = CreateSut();
        await sut.StoreAsync(Rate(90.74m, Friday), default);

        RateFetchOutcome outcome = await sut.StoreAsync(Rate(90.81m, Friday.AddDays(3)), default);

        outcome.Should().Be(RateFetchOutcome.Stored);
        _db.ExchangeRates.Should().HaveCount(2);
    }
}
