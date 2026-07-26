using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Auth;

public class LeaveStudioHandlerTests
{
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Client();

    private LeaveStudioHandler CreateSut() => new(
        _identity, _currentUser, NullLogger<LeaveStudioHandler>.Instance);

    private void UserHasTenantIds(params Guid[] studioIds) =>
        _identity.GetTenantIdsAsync(_currentUser.UserId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Guid>)studioIds);

    [Fact]
    public async Task Handle_LeavesNonActiveStudio_ReturnsIsLeavingActiveTenantFalse()
    {
        Guid studioId = Guid.NewGuid();
        Guid activeStudioId = Guid.NewGuid();
        UserHasTenantIds(studioId, activeStudioId);
        _identity.GetActiveTenantIdAsync(_currentUser.UserId, Arg.Any<CancellationToken>())
            .Returns(activeStudioId);

        LeaveStudioResponse response = await CreateSut().Handle(new LeaveStudioCommand(studioId), default);

        response.IsLeavingActiveTenant.Should().BeFalse();
        await _identity.Received(1).RemoveTenantClaimAsync(_currentUser.UserId, studioId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LeavesActiveStudio_ReturnsIsLeavingActiveTenantTrue()
    {
        Guid studioId = Guid.NewGuid();
        UserHasTenantIds(studioId);
        _identity.GetActiveTenantIdAsync(_currentUser.UserId, Arg.Any<CancellationToken>())
            .Returns(studioId);

        LeaveStudioResponse response = await CreateSut().Handle(new LeaveStudioCommand(studioId), default);

        response.IsLeavingActiveTenant.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NoMembershipForStudio_ThrowsNotFoundException()
    {
        UserHasTenantIds(Guid.NewGuid());

        Func<Task> act = () => CreateSut().Handle(new LeaveStudioCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
        await _identity.DidNotReceive().RemoveTenantClaimAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
