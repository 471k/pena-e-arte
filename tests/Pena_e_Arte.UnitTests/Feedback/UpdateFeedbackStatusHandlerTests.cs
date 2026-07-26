using FluentAssertions;
using Pena_e_Arte.Application.Feedback.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Feedback;

public class UpdateFeedbackStatusHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private UpdateFeedbackStatusHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ValidRequest_UpdatesStatusAndIssuerNote()
    {
        Guid id = await SeedReport();

        FeedbackReportResponse result = await CreateSut().Handle(
            new UpdateFeedbackStatusCommand(id, new UpdateFeedbackStatusRequest("Reviewing", "Looking into it")),
            default);

        result.Status.Should().Be("Reviewing");
        result.IssuerNote.Should().Be("Looking into it");
        _db.FeedbackReports.Single(r => r.Id == id).Status.Should().Be(FeedbackStatus.Reviewing);
    }

    [Fact]
    public async Task Handle_ResolvingReport_SetsResolvedAt()
    {
        Guid id = await SeedReport();

        FeedbackReportResponse result = await CreateSut().Handle(
            new UpdateFeedbackStatusCommand(id, new UpdateFeedbackStatusRequest("Resolved", null)),
            default);

        result.ResolvedAt.Should().NotBeNull();
        result.ResolvedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Handle_ReopeningResolvedReport_ClearsResolvedAt()
    {
        Guid id = await SeedReport();
        await CreateSut().Handle(
            new UpdateFeedbackStatusCommand(id, new UpdateFeedbackStatusRequest("Resolved", null)), default);

        FeedbackReportResponse result = await CreateSut().Handle(
            new UpdateFeedbackStatusCommand(id, new UpdateFeedbackStatusRequest("Open", null)), default);

        result.ResolvedAt.Should().BeNull();
        _db.FeedbackReports.Single(r => r.Id == id).ResolvedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReportNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new UpdateFeedbackStatusCommand(Guid.NewGuid(), new UpdateFeedbackStatusRequest("Reviewing", null)),
            default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Guid> SeedReport()
    {
        FeedbackReport report = FeedbackReport.Create(
            studioId: Guid.NewGuid(),
            submitterUserId: Guid.NewGuid(),
            submitterRole: "owner",
            studioName: "Test Studio",
            type: FeedbackType.General,
            title: "Title",
            body: "Some feedback body here.");

        _db.FeedbackReports.Add(report);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return report.Id;
    }
}
