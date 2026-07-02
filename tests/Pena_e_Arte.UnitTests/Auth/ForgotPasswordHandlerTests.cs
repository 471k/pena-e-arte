using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Auth;

public class ForgotPasswordHandlerTests
{
    private readonly IIdentityService     _identity      = Substitute.For<IIdentityService>();
    private readonly IEmailRenderer       _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IAppSettings         _appSettings   = Substitute.For<IAppSettings>();

    public ForgotPasswordHandlerTests()
    {
        _appSettings.BaseUrl.Returns("https://penaearte.com");
        _emailRenderer.RenderPasswordReset(Arg.Any<string>()).Returns("<html>reset</html>");
    }

    private ForgotPasswordHandler CreateSut() => new(
        _identity, _emailRenderer, _notifications, _appSettings,
        NullLogger<ForgotPasswordHandler>.Instance);

    [Fact]
    public async Task Handle_ExistingAccount_SendsResetEmailWithTokenInUrl()
    {
        _identity.GeneratePasswordResetTokenAsync("user@example.com")
                  .Returns((true, "secret-token", (string?)null));

        await CreateSut().Handle(new ForgotPasswordCommand(new ForgotPasswordRequest("user@example.com")), default);

        await _notifications.Received(1).SendEmailAsync(
            "user@example.com", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _emailRenderer.Received(1).RenderPasswordReset(
            Arg.Is<string>(url => url.Contains("token=secret-token") && url.Contains("email=user%40example.com")));
    }

    [Fact]
    public async Task Handle_NoAccountForEmail_DoesNotSendEmail()
    {
        // IIdentityService returns (true, null, null) when the email doesn't match any
        // account — this must never send an email or otherwise reveal account existence.
        _identity.GeneratePasswordResetTokenAsync("nobody@example.com")
                  .Returns((true, (string?)null, (string?)null));

        await CreateSut().Handle(new ForgotPasswordCommand(new ForgotPasswordRequest("nobody@example.com")), default);

        await _notifications.DidNotReceive().SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotificationServiceThrows_DoesNotPropagate()
    {
        _identity.GeneratePasswordResetTokenAsync(Arg.Any<string>())
                  .Returns((true, "secret-token", (string?)null));
        _notifications.SendEmailAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("SMTP down"));

        Func<Task> act = () => CreateSut().Handle(
            new ForgotPasswordCommand(new ForgotPasswordRequest("user@example.com")), default);

        await act.Should().NotThrowAsync();
    }
}
