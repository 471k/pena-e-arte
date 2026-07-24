using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Auth;

public class ResetPasswordHandlerTests
{
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();

    private ResetPasswordHandler CreateSut() => new(_identity);

    [Fact]
    public async Task Handle_ValidReset_DoesNotThrow()
    {
        _identity.ResetPasswordAsync("owner@test.com", "tok", "NewPass1!")
                 .Returns((true, Array.Empty<string>(), false));

        Func<Task> act = () => CreateSut().Handle(
            new ResetPasswordCommand(new ResetPasswordRequest("owner@test.com", "tok", "NewPass1!")), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_TokenInvalid_ThrowsPasswordResetTokenInvalidException()
    {
        _identity.ResetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                 .Returns((false, new[] { "Invalid token." }, true));

        Func<Task> act = () => CreateSut().Handle(
            new ResetPasswordCommand(new ResetPasswordRequest("owner@test.com", "expired-tok", "NewPass1!")), default);

        await act.Should().ThrowAsync<PasswordResetTokenInvalidException>();
    }

    [Fact]
    public async Task Handle_PasswordPolicyFailure_ThrowsBusinessRuleViolationException()
    {
        _identity.ResetPasswordAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                 .Returns((false, new[] { "Passwords must have at least one digit." }, false));

        Func<Task> act = () => CreateSut().Handle(
            new ResetPasswordCommand(new ResetPasswordRequest("owner@test.com", "tok", "weakpassword")), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("Passwords must have at least one digit.");
    }
}
