using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Contact.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Contact;

public class SubmitContactRequestValidatorTests
{
    private readonly SubmitContactRequestValidator _validator = new();

    private static SubmitContactRequestCommand Cmd(string name, string email, string message) =>
        new(new SubmitContactRequest(name, email, message));

    [Fact]
    public void Valid_Passes()
    {
        _validator.Validate(Cmd("Ana", "ana@example.com", "Hi there")).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "ana@example.com", "msg")]        // empty name
    [InlineData("Ana", "", "msg")]                     // empty email
    [InlineData("Ana", "not-an-email", "msg")]         // invalid email
    [InlineData("Ana", "ana@example.com", "")]         // empty message
    public void Invalid_Fails(string name, string email, string message)
    {
        _validator.Validate(Cmd(name, email, message)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void MessageOverMaxLength_Fails()
    {
        _validator.Validate(Cmd("Ana", "ana@example.com", new string('x', 2001)))
            .IsValid.Should().BeFalse();
    }
}

public class SubmitContactRequestHandlerTests
{
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();

    private SubmitContactRequestHandler CreateSut() =>
        new(_notifications, NullLogger<SubmitContactRequestHandler>.Instance);

    [Fact]
    public async Task Handle_SendsEmailToSupport_WithSubmitterAsReplyTo()
    {
        SubmitContactRequest req = new("Ana Costa", "ana@example.com", "I have a question");

        await CreateSut().Handle(new SubmitContactRequestCommand(req), default);

        await _notifications.Received(1).SendEmailAsync(
            "support@tattooos.co",
            Arg.Any<string>(),
            Arg.Any<string>(),
            "ana@example.com",           // Reply-To = submitter
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_HtmlEncodesUserInput_InTheEmailBody()
    {
        SubmitContactRequest req = new("<script>", "ana@example.com", "hi <b>bold</b>");

        string? capturedBody = null;
        await _notifications.SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Do<string>(b => capturedBody = b),
            Arg.Any<string>(), Arg.Any<CancellationToken>());

        await CreateSut().Handle(new SubmitContactRequestCommand(req), default);

        capturedBody.Should().NotBeNull();
        capturedBody!.Should().NotContain("<script>");
        capturedBody.Should().Contain("&lt;script&gt;");
    }
}
