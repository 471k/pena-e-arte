using NSubstitute;
using Pena_e_Arte.Application.Feedback.Commands;
using Pena_e_Arte.Application.Feedback.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Feedback;

public class SubmitFeedbackValidatorTests
{
    private readonly ICurrentUser   _user   = Substitute.For<ICurrentUser>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private SubmitFeedbackValidator Sut() => new(_user, _tenant);

    public SubmitFeedbackValidatorTests()
    {
        _user.Role.Returns("artist");
        _tenant.IsSet.Returns(true);
    }

    [Fact]
    public void Validate_ValidRequest_IsValid()
    {
        Sut().ShouldBeValid(Command("BugReport", "Broken button", "The submit button does nothing on Safari."));
    }

    [Fact]
    public void Validate_EmptyType_FailsOnType()
    {
        Sut().ShouldFailOn(Command("", "Title", "A description with enough characters."), "Request.Type");
    }

    [Fact]
    public void Validate_InvalidType_FailsOnType()
    {
        Sut().ShouldFailOn(Command("NotARealType", "Title", "A description with enough characters."), "Request.Type");
    }

    [Fact]
    public void Validate_EmptyTitle_FailsOnTitle()
    {
        Sut().ShouldFailOn(Command("General", "", "A description with enough characters."), "Request.Title");
    }

    [Fact]
    public void Validate_TitleExceedsMaxLength_FailsOnTitle()
    {
        Sut().ShouldFailOn(Command("General", new string('x', 151), "A description with enough characters."), "Request.Title");
    }

    [Fact]
    public void Validate_BodyBelowMinLength_FailsOnBody()
    {
        Sut().ShouldFailOn(Command("General", "Title", "short"), "Request.Body");
    }

    [Fact]
    public void Validate_BodyExceedsMaxLength_FailsOnBody()
    {
        Sut().ShouldFailOn(Command("General", "Title", new string('x', 2001)), "Request.Body");
    }

    [Fact]
    public void Validate_ArtistSubmittingSupportRequest_IsValid()
    {
        Sut().ShouldBeValid(Command("SupportRequest", "Need help", "A description with enough characters."));
    }

    [Fact]
    public void Validate_ClientSubmittingBugReport_FailsOnType()
    {
        _user.Role.Returns("client");
        Sut().ShouldFailOn(Command("BugReport", "Title", "A description with enough characters."), "Request.Type");
    }

    [Fact]
    public void Validate_ClientSubmittingFeatureRequest_FailsOnType()
    {
        _user.Role.Returns("client");
        Sut().ShouldFailOn(Command("FeatureRequest", "Title", "A description with enough characters."), "Request.Type");
    }

    [Fact]
    public void Validate_ClientSubmittingGeneral_FailsOnType()
    {
        _user.Role.Returns("client");
        Sut().ShouldFailOn(Command("General", "Title", "A description with enough characters."), "Request.Type");
    }

    [Fact]
    public void Validate_ClientSubmittingSupportRequest_IsValid()
    {
        _user.Role.Returns("client");
        Sut().ShouldBeValid(Command("SupportRequest", "Need help", "A description with enough characters."));
    }

    [Fact]
    public void Validate_OwnerSubmittingAnyType_IsValid()
    {
        _user.Role.Returns("owner");
        Sut().ShouldBeValid(Command("FeatureRequest", "Title", "A description with enough characters."));
    }

    [Fact]
    public void Validate_StudioLessClient_FailsOnStudio()
    {
        _user.Role.Returns("client");
        _tenant.IsSet.Returns(false);
        Sut().ShouldFailOn(Command("SupportRequest", "Need help", "A description with enough characters."), "Studio");
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

public class PostFeedbackMessageValidatorTests
{
    private readonly PostFeedbackMessageValidator _sut = new();

    [Fact]
    public void Validate_ValidRequest_IsValid()
    {
        _sut.ShouldBeValid(Command("Any updates on this?"));
    }

    [Fact]
    public void Validate_EmptyBody_FailsOnBody()
    {
        _sut.ShouldFailOn(Command(""), "Request.Body");
    }

    [Fact]
    public void Validate_BodyExceedsMaxLength_FailsOnBody()
    {
        _sut.ShouldFailOn(Command(new string('x', 2001)), "Request.Body");
    }

    private static PostFeedbackMessageCommand Command(string body) =>
        new(Guid.NewGuid(), new PostFeedbackMessageRequest(body));
}
