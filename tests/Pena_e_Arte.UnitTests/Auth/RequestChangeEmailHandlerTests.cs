using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Auth;

public class RequestChangeEmailHandlerTests
{
    private readonly IIdentityService                       _identity      = Substitute.For<IIdentityService>();
    private readonly IEmailRenderer                          _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService                    _notifications = Substitute.For<INotificationService>();
    private readonly IAppSettings                             _appSettings   = Substitute.For<IAppSettings>();
    private readonly ILogger<RequestChangeEmailHandler>       _logger        = Substitute.For<ILogger<RequestChangeEmailHandler>>();
    private readonly Guid                                     _userId        = Guid.NewGuid();

    public RequestChangeEmailHandlerTests()
    {
        _appSettings.BaseUrl.Returns("https://tattooos.co");
    }

    private RequestChangeEmailHandler CreateSut() =>
        new(_identity, _emailRenderer, _notifications, _appSettings, _logger);

    [Fact]
    public async Task Handle_ValidRequest_SendsConfirmationToNewEmail()
    {
        _identity.GenerateChangeEmailTokenAsync(_userId, "Password1!", "new@test.com", Arg.Any<CancellationToken>())
                 .Returns((true, "the-token", Array.Empty<string>(), false));

        await CreateSut().Handle(new RequestChangeEmailCommand(_userId, "Password1!", "new@test.com"), default);

        await _notifications.Received(1).SendEmailAsync(
            "new@test.com", Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WrongPassword_ThrowsBusinessRuleViolationException()
    {
        _identity.GenerateChangeEmailTokenAsync(_userId, "Wrong!", "new@test.com", Arg.Any<CancellationToken>())
                 .Returns((false, (string?)null, new[] { "Incorrect password." }, false));

        Func<Task> act = () => CreateSut().Handle(
            new RequestChangeEmailCommand(_userId, "Wrong!", "new@test.com"), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("Incorrect password.");
    }

    [Fact]
    public async Task Handle_EmailAlreadyTaken_ThrowsConflictException()
    {
        _identity.GenerateChangeEmailTokenAsync(_userId, "Password1!", "taken@test.com", Arg.Any<CancellationToken>())
                 .Returns((false, (string?)null, new[] { "That email is already in use." }, true));

        Func<Task> act = () => CreateSut().Handle(
            new RequestChangeEmailCommand(_userId, "Password1!", "taken@test.com"), default);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Handle_EmailSendFails_ThrowsServiceUnavailableException()
    {
        _identity.GenerateChangeEmailTokenAsync(_userId, "Password1!", "new@test.com", Arg.Any<CancellationToken>())
                 .Returns((true, "the-token", Array.Empty<string>(), false));
        _notifications.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                      .ThrowsAsync(new Exception("SMTP down"));

        Func<Task> act = () => CreateSut().Handle(
            new RequestChangeEmailCommand(_userId, "Password1!", "new@test.com"), default);

        await act.Should().ThrowAsync<ServiceUnavailableException>();
    }

    [Fact]
    public async Task Handle_ValidRequest_BuildsConfirmUrlWithTokenAndNewEmail()
    {
        _identity.GenerateChangeEmailTokenAsync(_userId, "Password1!", "new@test.com", Arg.Any<CancellationToken>())
                 .Returns((true, "abc123", Array.Empty<string>(), false));

        await CreateSut().Handle(new RequestChangeEmailCommand(_userId, "Password1!", "new@test.com"), default);

        _emailRenderer.Received(1).RenderChangeEmailConfirmation(
            Arg.Is<string>(url =>
                url.Contains("confirm-change-email") &&
                url.Contains("token=abc123") &&
                url.Contains($"userId={_userId}") &&
                url.Contains("newEmail=new%40test.com")));
    }
}
