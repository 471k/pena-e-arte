using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Onboarding.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Onboarding;

public class GetOnboardingTourStatusHandlerTests
{
    private readonly FakeDbContext _db   = FakeDbContext.Create();
    private readonly ICurrentUser  _user = Substitute.For<ICurrentUser>();
    private readonly Guid          _userId = Guid.NewGuid();

    public GetOnboardingTourStatusHandlerTests()
    {
        _user.UserId.Returns(_userId);
        _user.Role.Returns("client");
    }

    private GetOnboardingTourStatusHandler CreateSut() => new(_db, _user);

    [Fact]
    public async Task Handle_NoState_ReturnsNotCompleted()
    {
        OnboardingTourStatusResponse result = await CreateSut().Handle(new GetOnboardingTourStatusQuery("client"), default);

        result.HasCompletedTour.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_CompletedState_ReturnsCompleted()
    {
        UserOnboardingState state = UserOnboardingState.Create(_userId, "client");
        state.MarkComplete();
        _db.UserOnboardingStates.Add(state);
        await _db.SaveChangesAsync();

        OnboardingTourStatusResponse result = await CreateSut().Handle(new GetOnboardingTourStatusQuery("client"), default);

        result.HasCompletedTour.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_RoleDoesNotMatchCurrentUserRole_ThrowsForbidden()
    {
        Func<Task> act = () => CreateSut().Handle(new GetOnboardingTourStatusQuery("owner"), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
