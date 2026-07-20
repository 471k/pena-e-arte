using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Onboarding.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Onboarding;

public class MarkOnboardingTourCompleteHandlerTests
{
    private readonly FakeDbContext _db   = FakeDbContext.Create();
    private readonly ICurrentUser  _user = Substitute.For<ICurrentUser>();
    private readonly Guid          _userId = Guid.NewGuid();

    public MarkOnboardingTourCompleteHandlerTests()
    {
        _user.UserId.Returns(_userId);
        _user.Role.Returns("owner");
    }

    private MarkOnboardingTourCompleteHandler CreateSut() => new(_db, _user);

    [Fact]
    public async Task Handle_NoExistingState_CreatesAndMarksComplete()
    {
        await CreateSut().Handle(new MarkOnboardingTourCompleteCommand(new MarkOnboardingTourCompleteRequest("owner")), default);

        UserOnboardingState saved = _db.UserOnboardingStates.Single();
        saved.UserId.Should().Be(_userId);
        saved.Role.Should().Be("owner");
        saved.HasCompletedTour.Should().BeTrue();
        saved.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ExistingIncompleteState_MarksItComplete_DoesNotCreateDuplicate()
    {
        _db.UserOnboardingStates.Add(UserOnboardingState.Create(_userId, "owner"));
        await _db.SaveChangesAsync();

        await CreateSut().Handle(new MarkOnboardingTourCompleteCommand(new MarkOnboardingTourCompleteRequest("owner")), default);

        _db.UserOnboardingStates.Should().ContainSingle();
        _db.UserOnboardingStates.Single().HasCompletedTour.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AlreadyCompleted_IsIdempotent()
    {
        UserOnboardingState state = UserOnboardingState.Create(_userId, "owner");
        state.MarkComplete();
        _db.UserOnboardingStates.Add(state);
        await _db.SaveChangesAsync();
        DateTime? firstCompletedAt = state.CompletedAt;

        await CreateSut().Handle(new MarkOnboardingTourCompleteCommand(new MarkOnboardingTourCompleteRequest("owner")), default);

        _db.UserOnboardingStates.Should().ContainSingle();
        _db.UserOnboardingStates.Single().HasCompletedTour.Should().BeTrue();
        firstCompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_RoleDoesNotMatchCurrentUserRole_ThrowsForbidden()
    {
        Func<Task> act = () => CreateSut().Handle(
            new MarkOnboardingTourCompleteCommand(new MarkOnboardingTourCompleteRequest("issuer")), default);

        await act.Should().ThrowAsync<ForbiddenException>();
        _db.UserOnboardingStates.Should().BeEmpty();
    }
}
