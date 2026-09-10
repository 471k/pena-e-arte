using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Jobs;

public class BoothRentChargeJobTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();

    private BoothRentChargeJob CreateSut() => new(_db, NullLogger<BoothRentChargeJob>.Instance);

    [Fact]
    public async Task RunAsync_DueWeeklySchedule_CreatesExactlyOneCharge()
    {
        await SeedArtist();
        BoothRentSchedule schedule = await SeedSchedule(RentFrequency.Weekly, DateTime.UtcNow.AddDays(-1));

        await CreateSut().RunAsync();

        _db.BoothRentCharges.Count(c => c.BoothRentScheduleId == schedule.Id).Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_DueWeeklySchedule_AdvancesNextChargeDateBySevenDays()
    {
        await SeedArtist();
        DateTime due = DateTime.UtcNow.AddDays(-1);
        BoothRentSchedule schedule = await SeedSchedule(RentFrequency.Weekly, due);

        await CreateSut().RunAsync();

        _db.BoothRentSchedules.Single(s => s.Id == schedule.Id).NextChargeDate
            .Should().BeCloseTo(due.AddDays(7), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RunAsync_DueMonthlySchedule_AdvancesNextChargeDateByOneMonth()
    {
        await SeedArtist();
        DateTime due = DateTime.UtcNow.AddDays(-1);
        BoothRentSchedule schedule = await SeedSchedule(RentFrequency.Monthly, due);

        await CreateSut().RunAsync();

        _db.BoothRentSchedules.Single(s => s.Id == schedule.Id).NextChargeDate
            .Should().BeCloseTo(due.AddMonths(1), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RunAsync_RunTwiceSameDay_DoesNotDoubleCharge()
    {
        await SeedArtist();
        await SeedSchedule(RentFrequency.Weekly, DateTime.UtcNow.AddDays(-1));

        BoothRentChargeJob sut = CreateSut();
        await sut.RunAsync();
        await sut.RunAsync();

        _db.BoothRentCharges.Count().Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_NotYetDue_CreatesNoCharge()
    {
        await SeedArtist();
        await SeedSchedule(RentFrequency.Weekly, DateTime.UtcNow.AddDays(5));

        await CreateSut().RunAsync();

        _db.BoothRentCharges.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_InactiveSchedule_CreatesNoCharge()
    {
        await SeedArtist();
        BoothRentSchedule schedule = await SeedSchedule(RentFrequency.Weekly, DateTime.UtcNow.AddDays(-1));
        schedule.IsActive = false;
        await _db.SaveChangesAsync(default);

        await CreateSut().RunAsync();

        _db.BoothRentCharges.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_DueSchedule_ChargeAmountMatchesScheduleAmount()
    {
        await SeedArtist();
        await SeedSchedule(RentFrequency.Weekly, DateTime.UtcNow.AddDays(-1), amount: 75m);

        await CreateSut().RunAsync();

        _db.BoothRentCharges.Single().Amount.Should().Be(75m);
    }

    private async Task SeedArtist()
    {
        _db.Artists.Add(new Artist
        {
            Id = _artistId, StudioId = _studioId, FirstName = "A", LastName = "B", Email = "a@b.com",
        });
        await _db.SaveChangesAsync(default);
    }

    private async Task<BoothRentSchedule> SeedSchedule(
        RentFrequency frequency, DateTime nextChargeDate, decimal amount = 50m)
    {
        BoothRentSchedule schedule = new()
        {
            StudioId = _studioId,
            ArtistId = _artistId,
            AmountFixed = amount,
            Frequency = frequency,
            NextChargeDate = nextChargeDate,
            IsActive = true,
        };
        _db.BoothRentSchedules.Add(schedule);
        await _db.SaveChangesAsync(default);
        return schedule;
    }
}
