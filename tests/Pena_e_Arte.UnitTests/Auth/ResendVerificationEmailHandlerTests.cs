using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Auth;

public class ResendVerificationEmailHandlerTests
{
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly IEmailRenderer _renderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IAppSettings _appSettings = Substitute.For<IAppSettings>();
    private readonly Guid _userId = Guid.NewGuid();

    public ResendVerificationEmailHandlerTests()
    {
        _appSettings.BaseUrl.Returns("https://app.example.com");
        _identity.GetUserEmailAsync(_userId, Arg.Any<CancellationToken>())
                 .Returns("user@test.com");
        _identity.IsEmailConfirmedAsync(_userId, Arg.Any<CancellationToken>())
                 .Returns(false);
        _identity.GenerateEmailConfirmationTokenAsync(_userId)
                 .Returns("conf-token-123");
    }

    private ResendVerificationEmailHandler CreateSut() =>
        new(_identity, _renderer, _notifications, _appSettings, NullLogger<ResendVerificationEmailHandler>.Instance);

    [Fact]
    public async Task Handle_UnconfirmedEmail_SendsEmail()
    {
        await CreateSut().Handle(new ResendVerificationEmailCommand(_userId), default);

        await _notifications.Received(1).SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlreadyConfirmed_DoesNotSendEmail()
    {
        _identity.IsEmailConfirmedAsync(_userId, Arg.Any<CancellationToken>())
                 .Returns(true);

        await CreateSut().Handle(new ResendVerificationEmailCommand(_userId), default);

        await _notifications.DidNotReceiveWithAnyArgs()
            .SendEmailAsync(default!, default!, default!, default);
    }

    [Fact]
    public async Task Handle_GeneratesTokenWithCorrectUserId()
    {
        await CreateSut().Handle(new ResendVerificationEmailCommand(_userId), default);

        await _identity.Received(1).GenerateEmailConfirmationTokenAsync(_userId);
    }

    [Fact]
    public async Task Handle_DoesNotThrow_WhenEmailSendFails()
    {
        _notifications.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                      .Returns(Task.FromException(new Exception("SMTP error")));

        Func<Task> act = () => CreateSut().Handle(new ResendVerificationEmailCommand(_userId), default);

        await act.Should().NotThrowAsync();
    }
}
