using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Feedback.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Feedback;

public class PostFeedbackMessageHandlerTests
{
    private readonly FakeDbContext    _db       = FakeDbContext.Create();
    private readonly ICurrentUser     _user     = Substitute.For<ICurrentUser>();
    private readonly ICurrentTenant   _tenant   = Substitute.For<ICurrentTenant>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly Guid             _ownerUserId = Guid.NewGuid();
    private readonly Guid             _studioId    = Guid.NewGuid();

    private PostFeedbackMessageHandler CreateSut() => new(_db, _user, _tenant, _realtime);

    private FeedbackReport SeedReport(FeedbackStatus status = FeedbackStatus.Open)
    {
        FeedbackReport report = FeedbackReport.Create(
            _studioId, _ownerUserId, "owner", "Ink Soul", FeedbackType.SupportRequest, "Title", "Body of the ticket.");
        if (status != FeedbackStatus.Open) report.UpdateStatus(status, null);
        _db.FeedbackReports.Add(report);
        return report;
    }

    [Fact]
    public async Task Handle_TicketOwner_CreatesMessage()
    {
        FeedbackReport report = SeedReport();
        await _db.SaveChangesAsync();
        _user.UserId.Returns(_ownerUserId);
        _user.Role.Returns("owner");
        _tenant.StudioId.Returns(_studioId);

        FeedbackMessageResponse result = await CreateSut().Handle(
            new PostFeedbackMessageCommand(report.Id, new PostFeedbackMessageRequest("Any update?")), default);

        result.Body.Should().Be("Any update?");
        _db.FeedbackMessages.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_TicketOwner_PushesSignalREventToTicketGroup()
    {
        FeedbackReport report = SeedReport();
        await _db.SaveChangesAsync();
        _user.UserId.Returns(_ownerUserId);
        _user.Role.Returns("owner");
        _tenant.StudioId.Returns(_studioId);

        await CreateSut().Handle(
            new PostFeedbackMessageCommand(report.Id, new PostFeedbackMessageRequest("Any update?")), default);

        await _realtime.Received(1).NotifyTicketAsync(
            report.Id, "SupportMessageReceived", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DifferentUser_ThrowsForbidden()
    {
        FeedbackReport report = SeedReport();
        await _db.SaveChangesAsync();
        _user.UserId.Returns(Guid.NewGuid());
        _user.Role.Returns("artist");
        _tenant.StudioId.Returns(_studioId);

        Func<Task> act = () => CreateSut().Handle(
            new PostFeedbackMessageCommand(report.Id, new PostFeedbackMessageRequest("Sneaky")), default);

        await act.Should().ThrowAsync<ForbiddenException>();
        _db.FeedbackMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnknownReportId_ThrowsNotFound()
    {
        _user.UserId.Returns(_ownerUserId);
        _user.Role.Returns("owner");
        _tenant.StudioId.Returns(_studioId);

        Func<Task> act = () => CreateSut().Handle(
            new PostFeedbackMessageCommand(Guid.NewGuid(), new PostFeedbackMessageRequest("Hi")), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_StudioSideReplyOnResolvedTicket_ReopensIt()
    {
        FeedbackReport report = SeedReport(FeedbackStatus.Resolved);
        await _db.SaveChangesAsync();
        _user.UserId.Returns(_ownerUserId);
        _user.Role.Returns("owner");
        _tenant.StudioId.Returns(_studioId);

        await CreateSut().Handle(
            new PostFeedbackMessageCommand(report.Id, new PostFeedbackMessageRequest("Still broken")), default);

        report.Status.Should().Be(FeedbackStatus.Open);
        report.ResolvedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_StudioSideReplyOnDismissedTicket_ReopensIt()
    {
        FeedbackReport report = SeedReport(FeedbackStatus.Dismissed);
        await _db.SaveChangesAsync();
        _user.UserId.Returns(_ownerUserId);
        _user.Role.Returns("owner");
        _tenant.StudioId.Returns(_studioId);

        await CreateSut().Handle(
            new PostFeedbackMessageCommand(report.Id, new PostFeedbackMessageRequest("Please reconsider")), default);

        report.Status.Should().Be(FeedbackStatus.Open);
    }

    [Fact]
    public async Task Handle_IssuerReplyOnResolvedTicket_DoesNotReopenIt()
    {
        FeedbackReport report = SeedReport(FeedbackStatus.Resolved);
        await _db.SaveChangesAsync();
        _user.UserId.Returns(Guid.NewGuid());
        _user.Role.Returns("issuer");
        _tenant.StudioId.Returns(Guid.NewGuid());

        await CreateSut().Handle(
            new PostFeedbackMessageCommand(report.Id, new PostFeedbackMessageRequest("Closing this out")), default);

        report.Status.Should().Be(FeedbackStatus.Resolved);
    }

    [Fact]
    public async Task Handle_ReplyOnOpenTicket_DoesNotChangeStatus()
    {
        FeedbackReport report = SeedReport(FeedbackStatus.Open);
        await _db.SaveChangesAsync();
        _user.UserId.Returns(_ownerUserId);
        _user.Role.Returns("owner");
        _tenant.StudioId.Returns(_studioId);

        await CreateSut().Handle(
            new PostFeedbackMessageCommand(report.Id, new PostFeedbackMessageRequest("Following up")), default);

        report.Status.Should().Be(FeedbackStatus.Open);
    }
}
