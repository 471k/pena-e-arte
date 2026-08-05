using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Jobs;

public class TrafficRollupJobTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ILogger<TrafficRollupJob> _logger = Substitute.For<ILogger<TrafficRollupJob>>();

    private TrafficRollupJob CreateSut() => new(_db, _logger);

    // TrafficEvent.CreatedAt defaults to "now" via the entity's factory and has a private
    // setter (append-only by design) — tests need arbitrary past dates, so this goes through
    // EF's change-tracker Property API, which writes the mapped column directly rather than
    // calling the CLR setter, exactly the standard trick for backdating an otherwise
    // immutable-from-outside timestamp in a test fixture.
    private async Task<TrafficEvent> SeedEventAsync(
        Guid visitorId, DateTime createdAt, Guid? studioId = null, string? role = null, string? countryCode = null)
    {
        TrafficEvent ev = TrafficEvent.Create(
            visitorId, null, role, studioId, "/discover",
            countryCode: countryCode, country: null, regionCode: null, region: null, city: null,
            postalCode: null, continentCode: null, continent: null,
            latitude: null, longitude: null, accuracyRadiusKm: null, timeZone: null,
            asnNumber: null, asnOrganization: null,
            ipHash: null, deviceType: "desktop", browser: "Chrome", os: "Windows");
        _db.TrafficEvents.Add(ev);
        await _db.SaveChangesAsync();
        _db.Entry(ev).Property(e => e.CreatedAt).CurrentValue = createdAt;
        await _db.SaveChangesAsync();
        return ev;
    }

    [Fact]
    public async Task RunAsync_AggregatesYesterdaysEventsByStudioRoleCountry()
    {
        DateTime yesterday = DateTime.UtcNow.Date.AddDays(-1).AddHours(10);
        Guid studioId = Guid.NewGuid();
        Guid visitor1 = Guid.NewGuid();
        Guid visitor2 = Guid.NewGuid();

        await SeedEventAsync(visitor1, yesterday, studioId, "client", "AL");
        await SeedEventAsync(visitor1, yesterday.AddMinutes(5), studioId, "client", "AL");
        await SeedEventAsync(visitor2, yesterday.AddMinutes(10), studioId, "client", "AL");

        await CreateSut().RunAsync();

        TrafficDailyAggregate bucket = _db.TrafficDailyAggregates.Single();
        bucket.StudioId.Should().Be(studioId);
        bucket.Role.Should().Be("client");
        bucket.CountryCode.Should().Be("AL");
        bucket.VisitCount.Should().Be(3);
        bucket.UniqueVisitorCount.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_SeparatesGuestFromRegisteredIntoDifferentBuckets()
    {
        DateTime yesterday = DateTime.UtcNow.Date.AddDays(-1).AddHours(10);
        Guid studioId = Guid.NewGuid();

        await SeedEventAsync(Guid.NewGuid(), yesterday, studioId, role: null, "AL");
        await SeedEventAsync(Guid.NewGuid(), yesterday, studioId, role: "owner", "AL");

        await CreateSut().RunAsync();

        _db.TrafficDailyAggregates.Should().HaveCount(2);
        _db.TrafficDailyAggregates.Should().Contain(a => a.Role == null);
        _db.TrafficDailyAggregates.Should().Contain(a => a.Role == "owner");
    }

    [Fact]
    public async Task RunAsync_PurgesRawEventsOlderThan35Days_KeepsNewerOnes()
    {
        DateTime old = DateTime.UtcNow.AddDays(-40);
        DateTime recent = DateTime.UtcNow.AddDays(-10);

        TrafficEvent oldEvent = await SeedEventAsync(Guid.NewGuid(), old);
        TrafficEvent recentEvent = await SeedEventAsync(Guid.NewGuid(), recent);

        await CreateSut().RunAsync();

        _db.TrafficEvents.Find(oldEvent.Id).Should().BeNull();
        _db.TrafficEvents.Find(recentEvent.Id).Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_RunTwiceForSameDay_OverwritesRatherThanDoubleCounts()
    {
        DateTime yesterday = DateTime.UtcNow.Date.AddDays(-1).AddHours(10);
        Guid studioId = Guid.NewGuid();
        await SeedEventAsync(Guid.NewGuid(), yesterday, studioId, "client", "AL");

        await CreateSut().RunAsync();
        await CreateSut().RunAsync();

        _db.TrafficDailyAggregates.Should().HaveCount(1);
        _db.TrafficDailyAggregates.Single().VisitCount.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_NoEventsYesterday_CreatesNoAggregateRows()
    {
        await CreateSut().RunAsync();

        _db.TrafficDailyAggregates.Should().BeEmpty();
    }
}
