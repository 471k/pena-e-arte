using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Auth;

public class RegisterOAuthUserCommandTests
{
    private readonly IOAuthTokenValidator _validator = Substitute.For<IOAuthTokenValidator>();
    private readonly IIdentityService     _identity  = Substitute.For<IIdentityService>();
    private readonly FakeDbContext        _db        = FakeDbContext.Create();
    private readonly Guid                 _userId    = Guid.NewGuid();

    private RegisterOAuthUserHandler CreateSut() => new(_validator, _identity, _db);

    private void ValidatorReturns(string email, string? firstName = "Rui") =>
        _validator.ValidateGoogleTokenAsync("google-token", Arg.Any<CancellationToken>())
            .Returns(new OAuthUserInfo(email, "sub-1", firstName));

    private void IdentitySucceeds() =>
        _identity.CreateOAuthUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>())
                 .Returns((true, _userId, Array.Empty<string>()));

    [Fact]
    public async Task Handle_ClientRole_DoesNotThrow()
    {
        Guid studioId = Guid.NewGuid();
        ValidatorReturns("client@example.com");
        IdentitySucceeds();

        Func<Task> act = () => CreateSut().Handle(
            new RegisterOAuthUserCommand(new RegisterOAuthUserRequest("google", "google-token", "client", studioId)),
            default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_IdentityFailure_ThrowsBusinessRuleViolationException()
    {
        ValidatorReturns("client@example.com");
        _identity.CreateOAuthUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>())
                 .Returns((false, Guid.Empty, new[] { "Email already taken." }));

        Func<Task> act = () => CreateSut().Handle(
            new RegisterOAuthUserCommand(new RegisterOAuthUserRequest("google", "google-token", "client", Guid.NewGuid())),
            default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*Email already taken*");
    }

    [Fact]
    public async Task Handle_UnknownProvider_ThrowsBusinessRuleViolationException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new RegisterOAuthUserCommand(new RegisterOAuthUserRequest("facebook", "token", "client", Guid.NewGuid())),
            default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ClientRoleWithExistingClientRecord_LinksUserId()
    {
        Guid studioId = Guid.NewGuid();
        Client preCreated = new()
        {
            StudioId  = studioId,
            FirstName = "Pre",
            LastName  = "Created",
            Email     = "client@example.com",
        };
        _db.Clients.Add(preCreated);
        await _db.SaveChangesAsync();
        ValidatorReturns("client@example.com");
        IdentitySucceeds();

        await CreateSut().Handle(
            new RegisterOAuthUserCommand(new RegisterOAuthUserRequest("google", "google-token", "client", studioId)),
            default);

        _db.Clients.Single(c => c.Id == preCreated.Id).UserId.Should().Be(_userId);
    }

    [Fact]
    public async Task Handle_ClientRoleWithoutClientRecord_CreatesLinkedClient()
    {
        Guid studioId = Guid.NewGuid();
        ValidatorReturns("new.client@example.com");
        IdentitySucceeds();

        await CreateSut().Handle(
            new RegisterOAuthUserCommand(new RegisterOAuthUserRequest("google", "google-token", "client", studioId)),
            default);

        Client created = _db.Clients.Single(c => c.Email == "new.client@example.com");
        created.UserId.Should().Be(_userId);
        created.StudioId.Should().Be(studioId);
    }

    // Same rule as RegisterUserHandlerTests: owner self-registration via OAuth must be
    // bound to the studio's declared OwnerEmail — this predates the OAuth prompt and was
    // missing from the original spec, so it's enforced here to match RegisterUserHandler.
    [Fact]
    public async Task Handle_OwnerRoleWithMatchingStudioOwnerEmail_Succeeds()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, OwnerEmail = "owner@studio.com" });
        await _db.SaveChangesAsync();
        ValidatorReturns("owner@studio.com");
        IdentitySucceeds();

        Func<Task> act = () => CreateSut().Handle(
            new RegisterOAuthUserCommand(new RegisterOAuthUserRequest("google", "google-token", "owner", studioId)),
            default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_OwnerRoleWithMismatchedEmail_ThrowsUnauthorized()
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, OwnerEmail = "realowner@studio.com" });
        await _db.SaveChangesAsync();
        ValidatorReturns("attacker@evil.com");
        IdentitySucceeds();

        Func<Task> act = () => CreateSut().Handle(
            new RegisterOAuthUserCommand(new RegisterOAuthUserRequest("google", "google-token", "owner", studioId)),
            default);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        await _identity.DidNotReceive().CreateOAuthUserAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task Handle_OwnerRoleWithNonExistentStudio_ThrowsUnauthorized()
    {
        ValidatorReturns("owner@studio.com");

        Func<Task> act = () => CreateSut().Handle(
            new RegisterOAuthUserCommand(new RegisterOAuthUserRequest("google", "google-token", "owner", Guid.NewGuid())),
            default);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
