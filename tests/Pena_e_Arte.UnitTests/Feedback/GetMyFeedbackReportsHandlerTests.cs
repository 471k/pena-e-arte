using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Feedback.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Feedback;

public class GetMyFeedbackReportsHandlerTests
{
    private readonly FakeDbContext _db     = FakeDbContext.Create();
    private readonly ICurrentUser  _user   = Substitute.For<ICurrentUser>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid          _userId   = Guid.NewGuid();
    private readonly Guid          _studioId = Guid.NewGuid();

    public GetMyFeedbackReportsHandlerTests()
    {
        _user.UserId.Returns(_userId);
        _tenant.StudioId.Returns(_studioId);
    }

    private GetMyFeedbackReportsHandler CreateSut() => new(_db, _user, _tenant);

    [Fact]
    public async Task Handle_ReturnsOnlyReportsSubmittedByCurrentUserInCurrentStudio()
    {
        AddReport(_userId, _studioId, "client", FeedbackType.SupportRequest);
        AddReport(Guid.NewGuid(), _studioId, "client", FeedbackType.SupportRequest); // other user
        AddReport(_userId, Guid.NewGuid(), "client", FeedbackType.SupportRequest);   // other studio
        await _db.SaveChangesAsync();

        List<FeedbackReportResponse> result = await CreateSut().Handle(new GetMyFeedbackReportsQuery(), default);

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_FiltersByType_WhenProvided()
    {
        AddReport(_userId, _studioId, "artist", FeedbackType.SupportRequest);
        AddReport(_userId, _studioId, "artist", FeedbackType.BugReport);
        await _db.SaveChangesAsync();

        List<FeedbackReportResponse> result =
            await CreateSut().Handle(new GetMyFeedbackReportsQuery("SupportRequest"), default);

        result.Should().ContainSingle();
        result[0].Type.Should().Be("SupportRequest");
    }

    [Fact]
    public async Task Handle_NoMatchingReports_ReturnsEmptyList()
    {
        List<FeedbackReportResponse> result = await CreateSut().Handle(new GetMyFeedbackReportsQuery(), default);
        result.Should().BeEmpty();
    }

    private void AddReport(Guid submitterUserId, Guid studioId, string role, FeedbackType type) =>
        _db.FeedbackReports.Add(FeedbackReport.Create(
            studioId, submitterUserId, role, "Some Studio", type, "Title", "Body of the report."));
}
