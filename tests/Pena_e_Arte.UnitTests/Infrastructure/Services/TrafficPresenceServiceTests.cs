using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.UnitTests.Helpers;
using StackExchange.Redis;

namespace Pena_e_Arte.UnitTests.Infrastructure.Services;

// Scope note: this is the first coverage for TrafficPresenceService, added alongside the
// lat/long fields it now reads. Kept narrow (the lat/long parse round-trip) rather than full
// class coverage — IBatch/IDatabase pipelining is expensive to mock convincingly with no real
// Redis fixture available in this test project, matching GeoIpServiceTests' own "flagged rather
// than faked" precedent for untested paths that need a real backing service.
public class TrafficPresenceServiceTests
{
    private static (IConnectionMultiplexer Redis, IDatabase Db, IBatch Batch) MockRedis()
    {
        IConnectionMultiplexer redis = Substitute.For<IConnectionMultiplexer>();
        IDatabase db = Substitute.For<IDatabase>();
        IBatch batch = Substitute.For<IBatch>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(db);
        db.CreateBatch(Arg.Any<object?>()).Returns(batch);
        return (redis, db, batch);
    }

    [Fact]
    public async Task ReadSnapshotAsync_HashCarriesLatLong_ParsesThemAsDoubles()
    {
        (IConnectionMultiplexer redis, IDatabase db, IBatch batch) = MockRedis();
        string visitorId = Guid.NewGuid().ToString();

        db.SortedSetRemoveRangeByScoreAsync(
                Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<Exclude>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(0L));
        db.SortedSetRangeByScoreAsync(
                Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<Exclude>(),
                Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(new RedisValue[] { visitorId }));

        HashEntry[] hash =
        [
            new HashEntry("role", ""),
            new HashEntry("studioId", ""),
            new HashEntry("path", "/discover"),
            new HashEntry("countryCode", "AL"),
            new HashEntry("city", "Tirana"),
            // Written by PublicEndpoints.RecordTrafficBeacon via
            // double.ToString(CultureInfo.InvariantCulture) — decimal point, no thousands separator.
            new HashEntry("latitude", "41.3275"),
            new HashEntry("longitude", "19.8187"),
            new HashEntry("deviceType", "desktop"),
            new HashEntry("browser", "Chrome"),
            new HashEntry("connectedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()),
        ];
        batch.HashGetAllAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(Task.FromResult(hash));

        TrafficPresenceService sut = new(redis, FakeDbContext.Create());

        TrafficPresenceSnapshot snapshot = await sut.ReadSnapshotAsync();

        snapshot.Visitors.Should().ContainSingle();
        TrafficPresenceVisitor visitor = snapshot.Visitors[0];
        visitor.Latitude.Should().Be(41.3275);
        visitor.Longitude.Should().Be(19.8187);
    }

    [Fact]
    public async Task ReadSnapshotAsync_HashHasNoLatLong_LeavesThemNull()
    {
        (IConnectionMultiplexer redis, IDatabase db, IBatch batch) = MockRedis();
        string visitorId = Guid.NewGuid().ToString();

        db.SortedSetRemoveRangeByScoreAsync(
                Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<Exclude>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(0L));
        db.SortedSetRangeByScoreAsync(
                Arg.Any<RedisKey>(), Arg.Any<double>(), Arg.Any<double>(), Arg.Any<Exclude>(),
                Arg.Any<Order>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CommandFlags>())
            .Returns(Task.FromResult(new RedisValue[] { visitorId }));

        HashEntry[] hash =
        [
            new HashEntry("role", ""),
            new HashEntry("studioId", ""),
            new HashEntry("path", "/discover"),
            new HashEntry("latitude", ""),
            new HashEntry("longitude", ""),
            new HashEntry("connectedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()),
        ];
        batch.HashGetAllAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(Task.FromResult(hash));

        TrafficPresenceService sut = new(redis, FakeDbContext.Create());

        TrafficPresenceSnapshot snapshot = await sut.ReadSnapshotAsync();

        TrafficPresenceVisitor visitor = snapshot.Visitors.Should().ContainSingle().Subject;
        visitor.Latitude.Should().BeNull();
        visitor.Longitude.Should().BeNull();
    }
}
