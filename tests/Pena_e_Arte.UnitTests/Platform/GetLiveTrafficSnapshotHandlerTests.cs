using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Platform;

public class GetLiveTrafficSnapshotHandlerTests
{
    private readonly ITrafficPresenceReader _reader = Substitute.For<ITrafficPresenceReader>();

    private GetLiveTrafficSnapshotHandler CreateSut() => new(_reader);

    [Fact]
    public async Task Handle_MapsPresenceSnapshotFieldsOntoResponseOneToOne()
    {
        DateTime connectedAt = DateTime.UtcNow.AddMinutes(-2);
        TrafficPresenceSnapshot snapshot = new(
            TotalActive: 2,
            GuestCount: 1,
            RoleCounts: new Dictionary<string, int> { ["owner"] = 1 },
            Visitors:
            [
                new TrafficPresenceVisitor(
                    "visitor-1", "owner", "studio-1", "Ink Society", "AL", "Tirana",
                    41.3275, 19.8187, "desktop", "Chrome", "/dashboard", connectedAt),
                new TrafficPresenceVisitor(
                    "visitor-2", null, null, null, null, null, null, null, "mobile", "Safari", "/discover", connectedAt),
            ]);
        _reader.ReadSnapshotAsync(Arg.Any<CancellationToken>()).Returns(snapshot);

        LiveTrafficSnapshotResponse result = await CreateSut().Handle(new GetLiveTrafficSnapshotQuery(), default);

        result.TotalActive.Should().Be(2);
        result.GuestCount.Should().Be(1);
        result.RoleCounts.Should().ContainKey("owner").WhoseValue.Should().Be(1);
        result.Visitors.Should().HaveCount(2);
        result.Visitors[0].VisitorId.Should().Be("visitor-1");
        result.Visitors[0].StudioName.Should().Be("Ink Society");
        result.Visitors[0].Latitude.Should().Be(41.3275);
        result.Visitors[0].Longitude.Should().Be(19.8187);
        result.Visitors[1].Role.Should().BeNull();
        result.Visitors[1].Latitude.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoActiveVisitors_ReturnsEmptySnapshot()
    {
        _reader.ReadSnapshotAsync(Arg.Any<CancellationToken>())
               .Returns(new TrafficPresenceSnapshot(0, 0, [], []));

        LiveTrafficSnapshotResponse result = await CreateSut().Handle(new GetLiveTrafficSnapshotQuery(), default);

        result.TotalActive.Should().Be(0);
        result.Visitors.Should().BeEmpty();
    }
}
