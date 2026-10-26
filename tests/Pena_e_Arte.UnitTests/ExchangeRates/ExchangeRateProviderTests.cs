using FluentAssertions;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services.ExchangeRates;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ExchangeRates;

public class ExchangeRateProviderTests
{
    private static readonly DateOnly Friday = new(2026, 10, 23);
    private readonly FakeDbContext _db = FakeDbContext.Create();

    // 11:00 UTC is 12:00 or 13:00 in Tirane, the same calendar day either way.
    private ExchangeRateProvider CreateSut(DateOnly todayTirane) =>
        new(_db, new FixedTimeProvider(new DateTimeOffset(todayTirane.ToDateTime(new TimeOnly(11, 0)), TimeSpan.Zero)));

    private async Task StoreAsync(decimal rate, DateOnly date)
    {
        _db.ExchangeRates.Add(ExchangeRate.Create("EUR", "ALL", date, rate, "BoA-Fix", DateTime.UtcNow, DateTime.UtcNow));
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetLatestAsync_NoRateStored_ReturnsNull() =>
        (await CreateSut(Friday).GetLatestAsync("EUR", "ALL", default)).Should().BeNull();

    [Fact]
    public async Task GetLatestAsync_ReturnsTheNewestRateDate()
    {
        await StoreAsync(90.70m, Friday.AddDays(-1));
        await StoreAsync(90.74m, Friday);

        ExchangeRateSnapshot? latest = await CreateSut(Friday).GetLatestAsync("EUR", "ALL", default);

        latest.Should().Be(new ExchangeRateSnapshot(90.74m, Friday, "BoA-Fix", IsStale: false));
    }

    [Fact]
    public async Task GetLatestAsync_SevenDaysOld_IsNotStale()
    {
        await StoreAsync(90.74m, Friday);

        (await CreateSut(Friday.AddDays(7)).GetLatestAsync("EUR", "ALL", default))!.IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task GetLatestAsync_EightDaysOld_IsStale()
    {
        await StoreAsync(90.74m, Friday);

        (await CreateSut(Friday.AddDays(8)).GetLatestAsync("EUR", "ALL", default))!.IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task GetLatestAsync_OtherPair_IsNotReturned()
    {
        await StoreAsync(90.74m, Friday);

        (await CreateSut(Friday).GetLatestAsync("USD", "ALL", default)).Should().BeNull();
    }
}
