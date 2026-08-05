using FluentAssertions;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class GetTrafficBreakdownHandlerTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly FakeDbContext _db;

    public GetTrafficBreakdownHandlerTests()
    {
        _db = FakeDbContext.Create(_dbName);
    }

    // The handler now takes IAppDbContextFactory (it runs its 5 aggregate queries concurrently,
    // each against its own short-lived context) rather than a single IAppDbContext — the fake
    // factory points at the same named in-memory database as `_db` so seeded data is visible.
    private GetTrafficBreakdownHandler CreateSut() => new(new FakeDbContextFactory(_dbName));

    private static TrafficEvent MakeEvent(
        string path = "/discover", string? deviceType = "desktop", string? browser = "Chrome",
        string? asnOrganization = null) =>
        TrafficEvent.Create(
            Guid.NewGuid(), null, null, null, path,
            countryCode: "AL", country: "Albania", regionCode: null, region: null, city: null,
            postalCode: null, continentCode: null, continent: null,
            latitude: null, longitude: null, accuracyRadiusKm: null, timeZone: null,
            asnNumber: null, asnOrganization: asnOrganization,
            ipHash: null, deviceType: deviceType, browser: browser, os: "Windows");

    [Fact]
    public async Task Handle_NoData_ReturnsAllEmptyLists()
    {
        TrafficBreakdownResponse result = await CreateSut().Handle(new GetTrafficBreakdownQuery(30), default);

        result.TopCountries.Should().BeEmpty();
        result.DeviceBreakdown.Should().BeEmpty();
        result.BrowserBreakdown.Should().BeEmpty();
        result.TopPages.Should().BeEmpty();
        result.TopNetworks.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AsnOrganizationPresent_GroupsIntoTopNetworksByCount()
    {
        _db.TrafficEvents.Add(MakeEvent(asnOrganization: "Example ISP"));
        _db.TrafficEvents.Add(MakeEvent(asnOrganization: "Example ISP"));
        _db.TrafficEvents.Add(MakeEvent(asnOrganization: "Other Networks Inc"));
        await _db.SaveChangesAsync();

        TrafficBreakdownResponse result = await CreateSut().Handle(new GetTrafficBreakdownQuery(30), default);

        result.TopNetworks.Should().HaveCount(2);
        result.TopNetworks[0].Name.Should().Be("Example ISP");
        result.TopNetworks[0].Count.Should().Be(2);
    }

    [Fact]
    public async Task Handle_NullAsnOrganization_IsExcludedFromTopNetworks()
    {
        _db.TrafficEvents.Add(MakeEvent(asnOrganization: null));
        await _db.SaveChangesAsync();

        TrafficBreakdownResponse result = await CreateSut().Handle(new GetTrafficBreakdownQuery(30), default);

        result.TopNetworks.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_DeviceAndBrowserAndPages_StillAggregateAlongsideNetworks()
    {
        _db.TrafficEvents.Add(MakeEvent(path: "/discover", deviceType: "mobile", browser: "Safari"));
        _db.TrafficEvents.Add(MakeEvent(path: "/discover", deviceType: "mobile", browser: "Safari"));
        await _db.SaveChangesAsync();

        TrafficBreakdownResponse result = await CreateSut().Handle(new GetTrafficBreakdownQuery(30), default);

        result.DeviceBreakdown.Should().ContainSingle(d => d.Name == "mobile" && d.Count == 2);
        result.BrowserBreakdown.Should().ContainSingle(b => b.Name == "Safari" && b.Count == 2);
        result.TopPages.Should().ContainSingle(p => p.Name == "/discover" && p.Count == 2);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(500, 90)]
    public async Task Handle_ClampsDaysToValidRange(int requestedDays, int expectedDays)
    {
        TrafficBreakdownResponse result = await CreateSut().Handle(new GetTrafficBreakdownQuery(requestedDays), default);

        result.Days.Should().Be(expectedDays);
    }
}
