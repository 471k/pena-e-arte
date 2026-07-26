using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Auth;

public class ConfirmChangeEmailHandlerTests
{
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IEmailRenderer _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly ILogger<ConfirmChangeEmailHandler> _logger = Substitute.For<ILogger<ConfirmChangeEmailHandler>>();
    private readonly Guid _userId = Guid.NewGuid();

    private ConfirmChangeEmailHandler CreateSut() => new(_identity, _emailRenderer, _notifications, _logger);

    [Fact]
    public async Task Handle_ValidToken_DoesNotThrow()
    {
        _identity.GetUserEmailAsync(_userId, Arg.Any<CancellationToken>()).Returns("old@test.com");
        _identity.ConfirmChangeEmailAsync(_userId, "new@test.com", "token", Arg.Any<CancellationToken>())
                 .Returns((true, Array.Empty<string>(), false, false));

        Func<Task> act = () => CreateSut().Handle(
            new ConfirmChangeEmailCommand(_userId, "new@test.com", "token"), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_ValidToken_NotifiesOldEmailAddress()
    {
        _identity.GetUserEmailAsync(_userId, Arg.Any<CancellationToken>()).Returns("old@test.com");
        _identity.ConfirmChangeEmailAsync(_userId, "new@test.com", "token", Arg.Any<CancellationToken>())
                 .Returns((true, Array.Empty<string>(), false, false));

        await CreateSut().Handle(new ConfirmChangeEmailCommand(_userId, "new@test.com", "token"), default);

        await _notifications.Received(1).SendEmailAsync(
            "old@test.com", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_InvalidToken_ThrowsChangeEmailTokenInvalidException()
    {
        _identity.GetUserEmailAsync(_userId, Arg.Any<CancellationToken>()).Returns("old@test.com");
        _identity.ConfirmChangeEmailAsync(_userId, "new@test.com", "bad-token", Arg.Any<CancellationToken>())
                 .Returns((false, new[] { "Invalid confirmation request." }, true, false));

        Func<Task> act = () => CreateSut().Handle(
            new ConfirmChangeEmailCommand(_userId, "new@test.com", "bad-token"), default);

        await act.Should().ThrowAsync<ChangeEmailTokenInvalidException>();
    }

    [Fact]
    public async Task Handle_EmailTakenByAnotherAccountSinceRequest_ThrowsConflictException()
    {
        _identity.GetUserEmailAsync(_userId, Arg.Any<CancellationToken>()).Returns("old@test.com");
        _identity.ConfirmChangeEmailAsync(_userId, "new@test.com", "token", Arg.Any<CancellationToken>())
                 .Returns((false, new[] { "That email is already in use." }, false, true));

        Func<Task> act = () => CreateSut().Handle(
            new ConfirmChangeEmailCommand(_userId, "new@test.com", "token"), default);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Handle_NotificationSendFails_StillSucceeds()
    {
        _identity.GetUserEmailAsync(_userId, Arg.Any<CancellationToken>()).Returns("old@test.com");
        _identity.ConfirmChangeEmailAsync(_userId, "new@test.com", "token", Arg.Any<CancellationToken>())
                 .Returns((true, Array.Empty<string>(), false, false));
        _notifications.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                      .ThrowsAsync(new Exception("SMTP down"));

        Func<Task> act = () => CreateSut().Handle(
            new ConfirmChangeEmailCommand(_userId, "new@test.com", "token"), default);

        await act.Should().NotThrowAsync();
    }
}
