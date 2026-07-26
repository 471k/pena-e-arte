using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Auth;

public class ChangePasswordHandlerTests
{
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly Guid _userId = Guid.NewGuid();

    private ChangePasswordHandler CreateSut() => new(_identity);

    [Fact]
    public async Task Handle_ValidChange_DoesNotThrow()
    {
        _identity.ChangePasswordAsync(_userId, "OldPass1!", "NewPass2!", Arg.Any<CancellationToken>())
                 .Returns((true, Array.Empty<string>()));

        Func<Task> act = () => CreateSut().Handle(
            new ChangePasswordCommand(_userId, "OldPass1!", "NewPass2!"), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_IdentityFailure_ThrowsBusinessRuleViolationException()
    {
        _identity.ChangePasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns((false, new[] { "Incorrect password." }));

        Func<Task> act = () => CreateSut().Handle(
            new ChangePasswordCommand(_userId, "Wrong!", "NewPass2!"), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("Incorrect password.");
    }

    [Fact]
    public async Task Handle_CallsIdentityServiceWithCorrectIds()
    {
        _identity.ChangePasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns((true, Array.Empty<string>()));

        await CreateSut().Handle(new ChangePasswordCommand(_userId, "Old1!", "New2@"), default);

        await _identity.Received(1).ChangePasswordAsync(_userId, "Old1!", "New2@", Arg.Any<CancellationToken>());
    }
}
