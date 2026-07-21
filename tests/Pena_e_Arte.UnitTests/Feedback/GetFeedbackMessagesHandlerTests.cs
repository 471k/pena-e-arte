using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Feedback.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Feedback;

public class GetFeedbackMessagesHandlerTests
{
    private readonly FakeDbContext   _db     = FakeDbContext.Create();
    private readonly ICurrentUser    _user   = Substitute.For<ICurrentUser>();
    private readonly ICurrentTenant  _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid            _ownerUserId = Guid.NewGuid();
    private readonly Guid            _studioId    = Guid.NewGuid();

    private GetFeedbackMessagesHandler CreateSut() => new(_db, _user, _tenant);

    private FeedbackReport SeedReport(Guid submitterUserId, Guid studioId)
    {
        FeedbackReport report = FeedbackReport.Create(
            studioId, submitterUserId, "owner", "Ink Soul", FeedbackType.SupportRequest, "Title", "Body of the ticket.");
        _db.FeedbackReports.Add(report);
        return report;
    }

    [Fact]
    public async Task Handle_TicketOwner_CanReadMessages()
    {
        FeedbackReport report = SeedReport(_ownerUserId, _studioId);
        _db.FeedbackMessages.Add(FeedbackMessage.Create(report.Id, _ownerUserId, "owner", "Hello"));
        await _db.SaveChangesAsync();

        _user.UserId.Returns(_ownerUserId);
        _user.Role.Returns("owner");
        _tenant.StudioId.Returns(_studioId);

        List<FeedbackMessageResponse> result = await CreateSut().Handle(new GetFeedbackMessagesQuery(report.Id), default);

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_Issuer_CanReadAnyTicketMessages_CrossTenant()
    {
        FeedbackReport report = SeedReport(_ownerUserId, _studioId);
        _db.FeedbackMessages.Add(FeedbackMessage.Create(report.Id, _ownerUserId, "owner", "Hello"));
        await _db.SaveChangesAsync();

        _user.UserId.Returns(Guid.NewGuid());
        _user.Role.Returns("issuer");
        _tenant.StudioId.Returns(Guid.NewGuid()); // issuer's tenant is irrelevant

        List<FeedbackMessageResponse> result = await CreateSut().Handle(new GetFeedbackMessagesQuery(report.Id), default);

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_DifferentUserSameStudio_ThrowsForbidden()
    {
        FeedbackReport report = SeedReport(_ownerUserId, _studioId);
        await _db.SaveChangesAsync();

        _user.UserId.Returns(Guid.NewGuid()); // different user
        _user.Role.Returns("artist");
        _tenant.StudioId.Returns(_studioId); // same studio

        Func<Task> act = () => CreateSut().Handle(new GetFeedbackMessagesQuery(report.Id), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Handle_SameUserDifferentStudio_ThrowsForbidden()
    {
        FeedbackReport report = SeedReport(_ownerUserId, _studioId);
        await _db.SaveChangesAsync();

        _user.UserId.Returns(_ownerUserId);
        _user.Role.Returns("owner");
        _tenant.StudioId.Returns(Guid.NewGuid()); // switched to a different studio

        Func<Task> act = () => CreateSut().Handle(new GetFeedbackMessagesQuery(report.Id), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Handle_UnknownReportId_ThrowsNotFound()
    {
        _user.UserId.Returns(_ownerUserId);
        _user.Role.Returns("owner");
        _tenant.StudioId.Returns(_studioId);

        Func<Task> act = () => CreateSut().Handle(new GetFeedbackMessagesQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_MessagesReturnedInChronologicalOrder()
    {
        FeedbackReport report = SeedReport(_ownerUserId, _studioId);
        FeedbackMessage first  = FeedbackMessage.Create(report.Id, _ownerUserId, "owner", "First");
        await Task.Delay(5);
        FeedbackMessage second = FeedbackMessage.Create(report.Id, _ownerUserId, "owner", "Second");
        _db.FeedbackMessages.Add(second);
        _db.FeedbackMessages.Add(first);
        await _db.SaveChangesAsync();

        _user.UserId.Returns(_ownerUserId);
        _user.Role.Returns("owner");
        _tenant.StudioId.Returns(_studioId);

        List<FeedbackMessageResponse> result = await CreateSut().Handle(new GetFeedbackMessagesQuery(report.Id), default);

        result.Select(m => m.Body).Should().ContainInOrder("First", "Second");
    }
}
