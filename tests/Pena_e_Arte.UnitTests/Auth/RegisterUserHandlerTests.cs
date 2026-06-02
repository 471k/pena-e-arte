using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Auth;

public class RegisterUserHandlerTests
{
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();

    private RegisterUserHandler CreateSut() => new(_identity);

    [Fact]
    public async Task Handle_ValidRequest_DoesNotThrow()
    {
        _identity.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>())
                 .Returns((true, Array.Empty<string>()));

        Func<Task> act = () => CreateSut().Handle(
            new RegisterUserCommand(ValidRequest()), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_IdentityFailure_ThrowsBusinessRuleViolationException()
    {
        _identity.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>())
                 .Returns((false, new[] { "Email already taken.", "Weak password." }));

        Func<Task> act = () => CreateSut().Handle(
            new RegisterUserCommand(ValidRequest()), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*Email already taken*")
            .WithMessage("*Weak password*");
    }

    [Fact]
    public async Task Handle_SingleIdentityError_ThrowsWithThatMessage()
    {
        _identity.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>())
                 .Returns((false, new[] { "Passwords must be at least 8 characters." }));

        Func<Task> act = () => CreateSut().Handle(
            new RegisterUserCommand(ValidRequest()), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("Passwords must be at least 8 characters.");
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsIdentityServiceWithCorrectArguments()
    {
        Guid studioId = Guid.NewGuid();
        _identity.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>())
                 .Returns((true, Array.Empty<string>()));

        await CreateSut().Handle(
            new RegisterUserCommand(new RegisterUserRequest("test@example.com", "Password1!", "artist", studioId)),
            default);

        await _identity.Received(1).CreateUserAsync("test@example.com", "Password1!", "artist", studioId);
    }

    private static RegisterUserRequest ValidRequest() =>
        new("test@example.com", "Password1!", "owner", Guid.NewGuid());
}
