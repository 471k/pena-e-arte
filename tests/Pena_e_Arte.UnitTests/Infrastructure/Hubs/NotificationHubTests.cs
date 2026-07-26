using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using Pena_e_Arte.Infrastructure.Hubs;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Infrastructure.Hubs;

public class NotificationHubTests
{
    [Fact]
    public async Task JoinStudio_CallerTenantMatchesRequestedStudio_AddsToGroup()
    {
        Guid studioId = Guid.NewGuid();
        IGroupManager groups = Substitute.For<IGroupManager>();
        NotificationHub hub = new()
        {
            Context = FakeHubCallerContext.Build("conn-1", studioId, "client"),
            Groups = groups,
        };

        await hub.JoinStudio(studioId.ToString());

        await groups.Received(1)
            .AddToGroupAsync("conn-1", $"studio:{studioId}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinStudio_CallerTenantDoesNotMatchRequestedStudio_DoesNotAddToGroup()
    {
        Guid callerStudioId = Guid.NewGuid();
        Guid otherStudioId = Guid.NewGuid();
        IGroupManager groups = Substitute.For<IGroupManager>();
        NotificationHub hub = new()
        {
            Context = FakeHubCallerContext.Build("conn-1", callerStudioId, "client"),
            Groups = groups,
        };

        await hub.JoinStudio(otherStudioId.ToString());

        await groups.DidNotReceive()
            .AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinStudio_IssuerRole_AddsToGroupRegardlessOfOwnTenant()
    {
        Guid otherStudioId = Guid.NewGuid();
        IGroupManager groups = Substitute.For<IGroupManager>();
        NotificationHub hub = new()
        {
            Context = FakeHubCallerContext.Build("conn-1", tenantId: null, role: "issuer"),
            Groups = groups,
        };

        await hub.JoinStudio(otherStudioId.ToString());

        await groups.Received(1)
            .AddToGroupAsync("conn-1", $"studio:{otherStudioId}", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task JoinStudio_MalformedStudioId_DoesNotThrowAndDoesNotAddToGroup()
    {
        IGroupManager groups = Substitute.For<IGroupManager>();
        NotificationHub hub = new()
        {
            Context = FakeHubCallerContext.Build("conn-1", Guid.NewGuid(), "client"),
            Groups = groups,
        };

        Func<Task> act = () => hub.JoinStudio("not-a-guid");

        await act.Should().NotThrowAsync();
        await groups.DidNotReceive()
            .AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LeaveStudio_AnyStudioId_RemovesFromGroup()
    {
        Guid studioId = Guid.NewGuid();
        IGroupManager groups = Substitute.For<IGroupManager>();
        NotificationHub hub = new()
        {
            Context = FakeHubCallerContext.Build("conn-1", studioId, "client"),
            Groups = groups,
        };

        await hub.LeaveStudio(studioId.ToString());

        await groups.Received(1)
            .RemoveFromGroupAsync("conn-1", $"studio:{studioId}", Arg.Any<CancellationToken>());
    }
}
