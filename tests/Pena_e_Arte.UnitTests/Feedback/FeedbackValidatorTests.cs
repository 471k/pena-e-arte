using Pena_e_Arte.Application.Feedback.Commands;
using Pena_e_Arte.Application.Feedback.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Feedback;

public class SubmitFeedbackValidatorTests
{
    private readonly SubmitFeedbackValidator _sut = new();

    [Fact]
    public void Validate_ValidRequest_IsValid()
    {
        _sut.ShouldBeValid(Command("BugReport", "Broken button", "The submit button does nothing on Safari."));
    }

    [Fact]
    public void Validate_EmptyType_FailsOnType()
    {
        _sut.ShouldFailOn(Command("", "Title", "A description with enough characters."), "Request.Type");
    }

    [Fact]
    public void Validate_InvalidType_FailsOnType()
    {
        _sut.ShouldFailOn(Command("NotARealType", "Title", "A description with enough characters."), "Request.Type");
    }

    [Fact]
    public void Validate_EmptyTitle_FailsOnTitle()
    {
        _sut.ShouldFailOn(Command("General", "", "A description with enough characters."), "Request.Title");
    }

    [Fact]
    public void Validate_TitleExceedsMaxLength_FailsOnTitle()
    {
        _sut.ShouldFailOn(Command("General", new string('x', 151), "A description with enough characters."), "Request.Title");
    }

    [Fact]
    public void Validate_BodyBelowMinLength_FailsOnBody()
    {
        _sut.ShouldFailOn(Command("General", "Title", "short"), "Request.Body");
    }

    [Fact]
    public void Validate_BodyExceedsMaxLength_FailsOnBody()
    {
        _sut.ShouldFailOn(Command("General", "Title", new string('x', 2001)), "Request.Body");
    }

    private static SubmitFeedbackCommand Command(string type, string title, string body) =>
        new(new SubmitFeedbackRequest(type, title, body));
}

public class UpdateFeedbackStatusValidatorTests
{
    private readonly UpdateFeedbackStatusValidator _sut = new();

    [Fact]
    public void Validate_ValidRequest_IsValid()
    {
        _sut.ShouldBeValid(Command("Reviewing", "Looking into it"));
    }

    [Fact]
    public void Validate_EmptyStatus_FailsOnStatus()
    {
        _sut.ShouldFailOn(Command("", null), "Request.Status");
    }

    [Fact]
    public void Validate_InvalidStatus_FailsOnStatus()
    {
        _sut.ShouldFailOn(Command("NotARealStatus", null), "Request.Status");
    }

    [Fact]
    public void Validate_NullIssuerNote_IsValid()
    {
        _sut.ShouldBeValid(Command("Open", null));
    }

    [Fact]
    public void Validate_IssuerNoteExceedsMaxLength_FailsOnIssuerNote()
    {
        _sut.ShouldFailOn(Command("Open", new string('x', 1001)), "Request.IssuerNote");
    }

    private static UpdateFeedbackStatusCommand Command(string status, string? issuerNote) =>
        new(Guid.NewGuid(), new UpdateFeedbackStatusRequest(status, issuerNote));
}
