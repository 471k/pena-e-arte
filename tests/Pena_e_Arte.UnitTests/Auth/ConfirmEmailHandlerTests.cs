using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Auth;

public class ConfirmEmailHandlerTests
{
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();

    private ConfirmEmailHandler CreateSut() => new(_identity);

    [Fact]
    public async Task Handle_ValidToken_DoesNotThrow()
    {
        Guid userId = Guid.NewGuid();
        _identity.ConfirmEmailAsync(userId, "valid-token", Arg.Any<CancellationToken>())
                 .Returns((true, Array.Empty<string>()));

        Func<Task> act = () => CreateSut().Handle(new ConfirmEmailCommand(userId, "valid-token"), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_InvalidToken_ThrowsBusinessRuleViolationException()
    {
        Guid userId = Guid.NewGuid();
        _identity.ConfirmEmailAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns((false, new[] { "Invalid token." }));

        Func<Task> act = () => CreateSut().Handle(new ConfirmEmailCommand(userId, "bad-token"), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("Invalid token.");
    }

    [Fact]
    public async Task Handle_CallsConfirmWithCorrectArguments()
    {
        Guid   userId = Guid.NewGuid();
        string token  = "tok123";
        _identity.ConfirmEmailAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                 .Returns((true, Array.Empty<string>()));

        await CreateSut().Handle(new ConfirmEmailCommand(userId, token), default);

        await _identity.Received(1).ConfirmEmailAsync(userId, token, Arg.Any<CancellationToken>());
    }
}
