using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Auth;

public class RegisterUserHandlerTests
{
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IEmailRenderer _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IAppSettings _appSettings = Substitute.For<IAppSettings>();
    private readonly Guid _userId = Guid.NewGuid();

    public RegisterUserHandlerTests()
    {
        _appSettings.BaseUrl.Returns(string.Empty);
        _identity.GenerateEmailConfirmationTokenAsync(Arg.Any<Guid>()).Returns("token");
    }

    private RegisterUserHandler CreateSut() => new(
        _identity, _db, _emailRenderer, _notifications, _appSettings,
        NullLogger<RegisterUserHandler>.Instance);

    private void IdentitySucceeds() =>
        _identity.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>())
                 .Returns((true, _userId, Array.Empty<string>()));

    [Fact]
    public async Task Handle_ValidRequest_DoesNotThrow()
    {
        IdentitySucceeds();

        Func<Task> act = () => CreateSut().Handle(
            new RegisterUserCommand(ValidRequest()), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_IdentityFailure_ThrowsBusinessRuleViolationException()
    {
        _identity.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>())
                 .Returns((false, Guid.Empty, new[] { "Email already taken.", "Weak password." }));

        Func<Task> act = () => CreateSut().Handle(
            new RegisterUserCommand(ValidRequest()), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*Email already taken*")
            .WithMessage("*Weak password*");
    }

    [Fact]
    public async Task Handle_SingleIdentityError_ThrowsWithThatMessage()
    {
        _identity.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>())
                 .Returns((false, Guid.Empty, new[] { "Passwords must be at least 8 characters." }));

        Func<Task> act = () => CreateSut().Handle(
            new RegisterUserCommand(ValidRequest()), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("Passwords must be at least 8 characters.");
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsIdentityServiceWithCorrectArguments()
    {
        Guid studioId = Guid.NewGuid();
        IdentitySucceeds();

        await CreateSut().Handle(
            new RegisterUserCommand(new RegisterUserRequest("test@example.com", "Password1!", "artist", studioId)),
            default);

        await _identity.Received(1).CreateUserAsync("test@example.com", "Password1!", "artist", studioId);
    }

    [Fact]
    public async Task Handle_ClientRoleWithExistingClientRecord_LinksUserId()
    {
        Guid studioId = Guid.NewGuid();
        Client preCreated = new()
        {
            StudioId = studioId,
            FirstName = "Pre",
            LastName = "Created",
            Email = "client@example.com",
        };
        _db.Clients.Add(preCreated);
        await _db.SaveChangesAsync();
        IdentitySucceeds();

        await CreateSut().Handle(
            new RegisterUserCommand(new RegisterUserRequest("client@example.com", "Password1!", "client", studioId)),
            default);

        _db.Clients.Single(c => c.Id == preCreated.Id).UserId.Should().Be(_userId);
    }

    [Fact]
    public async Task Handle_ClientRoleWithoutClientRecord_CreatesLinkedClient()
    {
        Guid studioId = Guid.NewGuid();
        IdentitySucceeds();

        await CreateSut().Handle(
            new RegisterUserCommand(new RegisterUserRequest("new.client@example.com", "Password1!", "client", studioId)),
            default);

        Client created = _db.Clients.Single(c => c.Email == "new.client@example.com");
        created.UserId.Should().Be(_userId);
        created.StudioId.Should().Be(studioId);
        created.FirstName.Should().Be("new.client");
    }

    [Fact]
    public async Task Handle_NonClientRole_DoesNotCreateClientRecord()
    {
        IdentitySucceeds();

        await CreateSut().Handle(
            new RegisterUserCommand(new RegisterUserRequest("artist@example.com", "Password1!", "artist", Guid.NewGuid())),
            default);

        _db.Clients.Any(c => c.Email == "artist@example.com").Should().BeFalse();
    }

    [Fact]
    public async Task Handle_OwnerRoleWithMatchingStudioOwnerEmail_Succeeds()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, OwnerEmail = "owner@studio.com" });
        await _db.SaveChangesAsync();
        IdentitySucceeds();

        Func<Task> act = () => CreateSut().Handle(
            new RegisterUserCommand(new RegisterUserRequest("owner@studio.com", "Password1!", "owner", studioId)),
            default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_OwnerRoleWithMismatchedEmail_ThrowsUnauthorized()
    {
        // Prevents a caller from claiming a "owner" account on a studio (discoverable
        // via /public/studios/nearby) that someone else already registered.
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, OwnerEmail = "realowner@studio.com" });
        await _db.SaveChangesAsync();
        IdentitySucceeds();

        Func<Task> act = () => CreateSut().Handle(
            new RegisterUserCommand(new RegisterUserRequest("attacker@evil.com", "Password1!", "owner", studioId)),
            default);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await _identity.DidNotReceive().CreateUserAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_ClientRoleWithNoStudioId_CreatesNoClientRecordAndPassesNullToIdentity()
    {
        IdentitySucceeds();

        await CreateSut().Handle(
            new RegisterUserCommand(new RegisterUserRequest("noStudio.client@example.com", "Password1!", "client", null)),
            default);

        _db.Clients.Any(c => c.Email == "noStudio.client@example.com").Should().BeFalse();
        await _identity.Received(1).CreateUserAsync("noStudio.client@example.com", "Password1!", "client", null);
    }

    [Fact]
    public async Task Handle_OwnerRoleWithNonExistentStudio_ThrowsUnauthorized()
    {
        Func<Task> act = () => CreateSut().Handle(
            new RegisterUserCommand(new RegisterUserRequest("test@example.com", "Password1!", "owner", Guid.NewGuid())),
            default);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    // "client" avoids tripping the owner/studio-ownership authorization check added
    // above — these generic tests exercise identity-service passthrough, not that rule.
    private static RegisterUserRequest ValidRequest() =>
        new("test@example.com", "Password1!", "client", Guid.NewGuid());
}
