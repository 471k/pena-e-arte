using FluentAssertions;
using Pena_e_Arte.Application.Notifications.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Notifications;

public class GetNotificationsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetNotificationsHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_NoFilters_ReturnsAllLogsForTenant()
    {
        Guid studioId = Guid.NewGuid();
        SeedLogs(studioId, count: 3);
        await _db.SaveChangesAsync();

        List<NotificationLogResponse> result = await CreateSut()
            .Handle(new GetNotificationsQuery(null, null, null, null), default);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task Handle_RecipientIdFilter_ReturnsOnlyMatchingLogs()
    {
        Guid studioId     = Guid.NewGuid();
        Guid recipientId  = Guid.NewGuid();
        Guid otherId      = Guid.NewGuid();

        _db.NotificationLogs.Add(BuildLog(studioId, recipientId,  NotificationChannel.Email));
        _db.NotificationLogs.Add(BuildLog(studioId, otherId,      NotificationChannel.Email));
        await _db.SaveChangesAsync();

        List<NotificationLogResponse> result = await CreateSut()
            .Handle(new GetNotificationsQuery(recipientId, null, null, null), default);

        result.Should().ContainSingle(n => n.RecipientId == recipientId);
    }

    [Fact]
    public async Task Handle_ChannelFilter_ReturnsOnlyMatchingChannel()
    {
        Guid studioId = Guid.NewGuid();
        _db.NotificationLogs.Add(BuildLog(studioId, Guid.NewGuid(), NotificationChannel.Email));
        _db.NotificationLogs.Add(BuildLog(studioId, Guid.NewGuid(), NotificationChannel.Sms));
        await _db.SaveChangesAsync();

        List<NotificationLogResponse> result = await CreateSut()
            .Handle(new GetNotificationsQuery(null, "Email", null, null), default);

        result.Should().ContainSingle();
        result[0].Channel.Should().Be("Email");
    }

    [Fact]
    public async Task Handle_FromFilter_ExcludesLogsSentBefore()
    {
        Guid studioId = Guid.NewGuid();
        DateTime cutoff = DateTime.UtcNow.AddDays(-1);

        _db.NotificationLogs.Add(BuildLog(studioId, Guid.NewGuid(), NotificationChannel.Email, sentAt: cutoff.AddHours(-1)));
        _db.NotificationLogs.Add(BuildLog(studioId, Guid.NewGuid(), NotificationChannel.Email, sentAt: cutoff.AddHours(1)));
        await _db.SaveChangesAsync();

        List<NotificationLogResponse> result = await CreateSut()
            .Handle(new GetNotificationsQuery(null, null, cutoff, null), default);

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_ToFilter_ExcludesLogsSentAfter()
    {
        Guid studioId = Guid.NewGuid();
        DateTime cutoff = DateTime.UtcNow;

        _db.NotificationLogs.Add(BuildLog(studioId, Guid.NewGuid(), NotificationChannel.Email, sentAt: cutoff.AddHours(-1)));
        _db.NotificationLogs.Add(BuildLog(studioId, Guid.NewGuid(), NotificationChannel.Email, sentAt: cutoff.AddHours(1)));
        await _db.SaveChangesAsync();

        List<NotificationLogResponse> result = await CreateSut()
            .Handle(new GetNotificationsQuery(null, null, null, cutoff), default);

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_NoLogs_ReturnsEmptyList()
    {
        List<NotificationLogResponse> result = await CreateSut()
            .Handle(new GetNotificationsQuery(null, null, null, null), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NoFilters_ReturnsMostRecentFirst()
    {
        Guid studioId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;

        _db.NotificationLogs.Add(BuildLog(studioId, Guid.NewGuid(), NotificationChannel.Email, sentAt: now.AddHours(-2)));
        _db.NotificationLogs.Add(BuildLog(studioId, Guid.NewGuid(), NotificationChannel.Email, sentAt: now.AddHours(-1)));
        _db.NotificationLogs.Add(BuildLog(studioId, Guid.NewGuid(), NotificationChannel.Email, sentAt: now));
        await _db.SaveChangesAsync();

        List<NotificationLogResponse> result = await CreateSut()
            .Handle(new GetNotificationsQuery(null, null, null, null), default);

        result[0].SentAt.Should().BeAfter(result[1].SentAt!.Value);
        result[1].SentAt.Should().BeAfter(result[2].SentAt!.Value);
    }

    [Fact]
    public async Task Handle_MapsAllFields()
    {
        Guid     studioId    = Guid.NewGuid();
        Guid     recipientId = Guid.NewGuid();
        DateTime sentAt      = DateTime.UtcNow;

        _db.NotificationLogs.Add(new NotificationLog
        {
            StudioId    = studioId,
            RecipientId = recipientId,
            Channel     = NotificationChannel.Email,
            Subject     = "Test Subject",
            Body        = "Test Body",
            SentAt      = sentAt,
            IsSuccess   = true
        });
        await _db.SaveChangesAsync();

        List<NotificationLogResponse> result = await CreateSut()
            .Handle(new GetNotificationsQuery(null, null, null, null), default);

        NotificationLogResponse item = result.Single();
        item.RecipientId.Should().Be(recipientId);
        item.Channel.Should().Be("Email");
        item.Subject.Should().Be("Test Subject");
        item.Body.Should().Be("Test Body");
        item.SentAt.Should().BeCloseTo(sentAt, TimeSpan.FromSeconds(1));
        item.IsSuccess.Should().BeTrue();
    }

    private void SeedLogs(Guid studioId, int count)
    {
        for (int i = 0; i < count; i++)
            _db.NotificationLogs.Add(BuildLog(studioId, Guid.NewGuid(), NotificationChannel.Email));
    }

    private static NotificationLog BuildLog(
        Guid studioId, Guid recipientId, NotificationChannel channel,
        DateTime? sentAt = null) => new()
    {
        StudioId    = studioId,
        RecipientId = recipientId,
        Channel     = channel,
        Subject     = "Subject",
        Body        = "Body",
        SentAt      = sentAt ?? DateTime.UtcNow,
        IsSuccess   = true
    };
}
