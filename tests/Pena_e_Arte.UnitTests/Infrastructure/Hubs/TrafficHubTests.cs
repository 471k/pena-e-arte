using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Hubs;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Infrastructure.Hubs;

public class TrafficHubTests
{
    [Fact]
    public async Task OnConnectedAsync_AddsConnectionToThePlatformTrafficGroup()
    {
        IGroupManager groups = Substitute.For<IGroupManager>();
        ITrafficConnectionCounter counter = Substitute.For<ITrafficConnectionCounter>();
        TrafficHub hub = new(counter)
        {
            Context = FakeHubCallerContext.Build("conn-1", null, "admin"),
            Groups = groups,
        };

        await hub.OnConnectedAsync();

        await groups.Received(1)
            .AddToGroupAsync("conn-1", "platform:traffic", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnConnectedAsync_IncrementsConnectionCounter()
    {
        ITrafficConnectionCounter counter = Substitute.For<ITrafficConnectionCounter>();
        TrafficHub hub = new(counter)
        {
            Context = FakeHubCallerContext.Build("conn-1", null, "admin"),
            Groups = Substitute.For<IGroupManager>(),
        };

        await hub.OnConnectedAsync();

        counter.Received(1).Increment();
    }

    [Fact]
    public async Task OnDisconnectedAsync_DecrementsConnectionCounter()
    {
        ITrafficConnectionCounter counter = Substitute.For<ITrafficConnectionCounter>();
        TrafficHub hub = new(counter)
        {
            Context = FakeHubCallerContext.Build("conn-1", null, "admin"),
            Groups = Substitute.For<IGroupManager>(),
        };

        await hub.OnDisconnectedAsync(null);

        counter.Received(1).Decrement();
    }
}
