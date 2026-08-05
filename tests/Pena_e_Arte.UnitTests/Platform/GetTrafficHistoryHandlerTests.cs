using FluentAssertions;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class GetTrafficHistoryHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetTrafficHistoryHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_NoData_ReturnsEmptyDataPoints()
    {
        TrafficHistoryResponse result = await CreateSut().Handle(new GetTrafficHistoryQuery(30), default);

        result.Days.Should().Be(30);
        result.DataPoints.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_SumsAcrossStudiosAndCountriesIntoOneRowPerDayPerRole()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        _db.TrafficDailyAggregates.Add(TrafficDailyAggregate.Create(today, Guid.NewGuid(), "client", "AL", 5, 3));
        _db.TrafficDailyAggregates.Add(TrafficDailyAggregate.Create(today, Guid.NewGuid(), "client", "GR", 2, 2));
        _db.TrafficDailyAggregates.Add(TrafficDailyAggregate.Create(today, null, null, "AL", 7, 6));
        await _db.SaveChangesAsync();

        TrafficHistoryResponse result = await CreateSut().Handle(new GetTrafficHistoryQuery(30), default);

        result.DataPoints.Should().HaveCount(1);
        TrafficHistoryDataPoint point = result.DataPoints[0];
        point.ClientCount.Should().Be(7);
        point.GuestCount.Should().Be(7);
        point.ArtistCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_OutsideRequestedWindow_IsExcluded()
    {
        DateOnly tooOld = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-40));
        _db.TrafficDailyAggregates.Add(TrafficDailyAggregate.Create(tooOld, null, "client", "AL", 5, 3));
        await _db.SaveChangesAsync();

        TrafficHistoryResponse result = await CreateSut().Handle(new GetTrafficHistoryQuery(30), default);

        result.DataPoints.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsDataPointsOrderedByDateAscending()
    {
        DateOnly day1 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2));
        DateOnly day2 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
        _db.TrafficDailyAggregates.Add(TrafficDailyAggregate.Create(day2, null, "client", "AL", 1, 1));
        _db.TrafficDailyAggregates.Add(TrafficDailyAggregate.Create(day1, null, "client", "AL", 1, 1));
        await _db.SaveChangesAsync();

        TrafficHistoryResponse result = await CreateSut().Handle(new GetTrafficHistoryQuery(30), default);

        result.DataPoints.Should().HaveCount(2);
        result.DataPoints[0].Date.Should().Be(day1);
        result.DataPoints[1].Date.Should().Be(day2);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(500, 90)]
    public async Task Handle_ClampsDaysToValidRange(int requestedDays, int expectedDays)
    {
        TrafficHistoryResponse result = await CreateSut().Handle(new GetTrafficHistoryQuery(requestedDays), default);

        result.Days.Should().Be(expectedDays);
    }
}
