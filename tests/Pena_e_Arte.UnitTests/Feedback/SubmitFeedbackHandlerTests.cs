using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Feedback.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Feedback;

public class SubmitFeedbackHandlerTests
{
    private readonly FakeDbContext  _db     = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser   _user   = Substitute.For<ICurrentUser>();
    private readonly Guid           _studioId = Guid.NewGuid();
    private readonly Guid           _userId   = Guid.NewGuid();

    public SubmitFeedbackHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _user.UserId.Returns(_userId);
        _user.Role.Returns("artist");
    }

    private SubmitFeedbackHandler CreateSut() => new(_db, _tenant, _user);

    [Fact]
    public async Task Handle_ValidRequest_CreatesAndPersistsReport()
    {
        await SeedStudio();
        SubmitFeedbackCommand command = Command("BugReport", "Broken button", "The submit button does nothing on Safari.");

        FeedbackReportResponse result = await CreateSut().Handle(command, default);

        FeedbackReport saved = _db.FeedbackReports.Single(r => r.Id == result.Id);
        saved.StudioId.Should().Be(_studioId);
        saved.SubmitterUserId.Should().Be(_userId);
        saved.SubmitterRole.Should().Be("artist");
        saved.StudioName.Should().Be("Test Studio");
    }

    [Fact]
    public async Task Handle_ReturnsCorrectResponse()
    {
        await SeedStudio();
        SubmitFeedbackCommand command = Command("BugReport", "Broken button", "The submit button does nothing on Safari.");

        FeedbackReportResponse result = await CreateSut().Handle(command, default);

        result.Type.Should().Be("BugReport");
        result.Title.Should().Be("Broken button");
        result.Body.Should().Be("The submit button does nothing on Safari.");
        result.Status.Should().Be("Open");
        result.StudioName.Should().Be("Test Studio");
        result.SubmitterRole.Should().Be("artist");
        result.IssuerNote.Should().BeNull();
        result.ResolvedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_TitleAndBodyWithWhitespace_AreTrimmedBeforeStorage()
    {
        await SeedStudio();
        SubmitFeedbackCommand command = Command("General", "  Padded title  ", "  Padded body with enough characters.  ");

        FeedbackReportResponse result = await CreateSut().Handle(command, default);

        result.Title.Should().Be("Padded title");
        result.Body.Should().Be("Padded body with enough characters.");
    }

    [Fact]
    public async Task Handle_StudioNotFound_ThrowsInvalidOperationException()
    {
        // No studio seeded — tenant.StudioId does not match any Studio row.
        SubmitFeedbackCommand command = Command("General", "Title", "Some feedback body here.");

        Func<Task> act = () => CreateSut().Handle(command, default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private async Task SeedStudio()
    {
        _db.Studios.Add(new Studio
        {
            Id             = _studioId,
            Name           = "Test Studio",
            Slug           = "test-studio",
            City           = "Porto",
            OwnerEmail     = "owner@test.com",
            IsActive       = true,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
        });
        await _db.SaveChangesAsync();
    }

    private static SubmitFeedbackCommand Command(string type, string title, string body) =>
        new(new SubmitFeedbackRequest(type, title, body));
}
