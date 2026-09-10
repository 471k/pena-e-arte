using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Support.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Support;

public class StartImpersonationHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Admin();
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();

    private StartImpersonationHandler CreateSut() => new(_db, _currentUser, _identity);

    [Fact]
    public async Task Handle_ValidStudio_CreatesActiveSessionAttributedToRealAdmin()
    {
        Guid studioId = await SeedStudioAsync("Ink & Iron");
        StubIdentitySuccess("fake-jwt");

        await CreateSut().Handle(
            new StartImpersonationCommand(studioId, new StartImpersonationRequest("Investigating ticket #42")), default);

        ImpersonationSession session = _db.ImpersonationSessions.Single();
        session.ActorUserId.Should().Be(_currentUser.UserId);
        session.StudioId.Should().Be(studioId);
        session.ReasonCode.Should().Be("Investigating ticket #42");
        session.EndedAt.Should().BeNull();
        session.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(45), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_ValidStudio_ReturnsTokenResponseFromIdentityService()
    {
        Guid studioId = await SeedStudioAsync("Skin Deep");
        StubIdentitySuccess("the-access-token");

        ImpersonationTokenResponse result = await CreateSut().Handle(
            new StartImpersonationCommand(studioId, new StartImpersonationRequest("Support ticket")), default);

        result.AccessToken.Should().Be("the-access-token");
        result.StudioId.Should().Be(studioId);
        result.StudioName.Should().Be("Skin Deep");
        result.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(45), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_PassesSessionIdAndExpiryToIdentityService()
    {
        Guid studioId = await SeedStudioAsync("Bold Line");
        StubIdentitySuccess("token");

        await CreateSut().Handle(
            new StartImpersonationCommand(studioId, new StartImpersonationRequest("Support ticket")), default);

        ImpersonationSession session = _db.ImpersonationSessions.Single();
        await _identity.Received(1).IssueImpersonationTokenAsync(
            _currentUser.UserId, studioId, session.Id, session.ExpiresAt);
    }

    [Fact]
    public async Task Handle_StudioNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new StartImpersonationCommand(Guid.NewGuid(), new StartImpersonationRequest("reason")), default);

        await act.Should().ThrowAsync<NotFoundException>();
        _db.ImpersonationSessions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_IdentityServiceFails_ThrowsBusinessRuleViolationException()
    {
        Guid studioId = await SeedStudioAsync("Failing Studio");
        _identity.IssueImpersonationTokenAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>())
            .Returns(Task.FromResult<(bool, string?, string?)>((false, null, "Admin account not found.")));

        Func<Task> act = () => CreateSut().Handle(
            new StartImpersonationCommand(studioId, new StartImpersonationRequest("reason")), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    private void StubIdentitySuccess(string token) =>
        _identity.IssueImpersonationTokenAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>())
            .Returns(Task.FromResult<(bool, string?, string?)>((true, token, null)));

    private async Task<Guid> SeedStudioAsync(string name)
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = name, Slug = $"{studioId:N}" });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return studioId;
    }
}

public class StartImpersonationValidatorTests
{
    private readonly StartImpersonationValidator _validator = new();

    [Fact]
    public void Validate_EmptyReasonCode_Fails() =>
        _validator.Validate(new StartImpersonationCommand(Guid.NewGuid(), new StartImpersonationRequest("")))
            .IsValid.Should().BeFalse();

    [Fact]
    public void Validate_TooShortReasonCode_Fails() =>
        _validator.Validate(new StartImpersonationCommand(Guid.NewGuid(), new StartImpersonationRequest("hi")))
            .IsValid.Should().BeFalse();

    [Fact]
    public void Validate_EmptyStudioId_Fails() =>
        _validator.Validate(new StartImpersonationCommand(Guid.Empty, new StartImpersonationRequest("Valid reason")))
            .IsValid.Should().BeFalse();

    [Fact]
    public void Validate_ValidCommand_Succeeds() =>
        _validator.Validate(new StartImpersonationCommand(Guid.NewGuid(), new StartImpersonationRequest("Investigating a booking issue")))
            .IsValid.Should().BeTrue();
}
