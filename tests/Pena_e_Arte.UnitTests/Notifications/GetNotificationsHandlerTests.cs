using FluentAssertions;
using Pena_e_Arte.Application.Notifications.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Notifications;

public class GetNotificationsHandlerTests
{
    private readonly FakeDbContext   _db          = FakeDbContext.Create();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Owner();

    private GetNotificationsHandler CreateSut() => new(_db, _currentUser);

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

    [Fact]
    public async Task Handle_ClientRecipient_ResolvesRecipientNameFromClient()
    {
        Guid studioId = Guid.NewGuid();
        Guid clientId = Guid.NewGuid();

        _db.Clients.Add(new Client
        {
            Id = clientId, StudioId = studioId,
            FirstName = "Ana", LastName = "Costa", Email = "ana@test.com"
        });
        _db.NotificationLogs.Add(BuildLog(
            studioId, clientId, NotificationChannel.Email, recipientType: NotificationRecipientType.Client));
        await _db.SaveChangesAsync();

        List<NotificationLogResponse> result = await CreateSut()
            .Handle(new GetNotificationsQuery(null, null, null, null), default);

        result.Single().RecipientName.Should().Be("Ana Costa");
    }

    [Fact]
    public async Task Handle_StudioRecipient_ResolvesRecipientNameFromStudio()
    {
        Guid studioId = Guid.NewGuid();

        _db.Studios.Add(new Studio { Id = studioId, Name = "Ink Soul", OwnerEmail = "owner@ink.test" });
        _db.NotificationLogs.Add(BuildLog(
            studioId, studioId, NotificationChannel.Email, recipientType: NotificationRecipientType.Studio));
        await _db.SaveChangesAsync();

        List<NotificationLogResponse> result = await CreateSut()
            .Handle(new GetNotificationsQuery(null, null, null, null), default);

        result.Single().RecipientName.Should().Be("Ink Soul");
    }

    [Fact]
    public async Task Handle_RecipientNoLongerExists_RecipientNameIsNull()
    {
        Guid studioId = Guid.NewGuid();
        Guid deletedClientId = Guid.NewGuid();

        // No matching Client row seeded — simulates a deleted/missing recipient.
        _db.NotificationLogs.Add(BuildLog(
            studioId, deletedClientId, NotificationChannel.Email, recipientType: NotificationRecipientType.Client));
        await _db.SaveChangesAsync();

        List<NotificationLogResponse> result = await CreateSut()
            .Handle(new GetNotificationsQuery(null, null, null, null), default);

        result.Single().RecipientName.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ArtistCaller_ReturnsOnlyOwnArtistNotifications()
    {
        FakeCurrentUser artistUser = FakeCurrentUser.Artist();
        Guid studioId = Guid.NewGuid();
        var artist = new Artist
        {
            StudioId  = studioId,
            UserId    = artistUser.UserId,
            FirstName = "Art",
            LastName  = "Ist",
            Email     = $"{Guid.NewGuid()}@test.com",
        };
        _db.Artists.Add(artist);

        _db.NotificationLogs.Add(BuildLog(studioId, artist.Id, NotificationChannel.Email, recipientType: NotificationRecipientType.Artist));
        _db.NotificationLogs.Add(BuildLog(studioId, Guid.NewGuid(), NotificationChannel.Email, recipientType: NotificationRecipientType.Artist));
        _db.NotificationLogs.Add(BuildLog(studioId, Guid.NewGuid(), NotificationChannel.Email, recipientType: NotificationRecipientType.Client));
        await _db.SaveChangesAsync();

        GetNotificationsHandler sut = new(_db, artistUser);
        List<NotificationLogResponse> result = await sut.Handle(new GetNotificationsQuery(null, null, null, null), default);

        result.Should().ContainSingle(n => n.RecipientId == artist.Id);
    }

    [Fact]
    public async Task Handle_ArtistCaller_IgnoresRequestedRecipientIdFilter()
    {
        FakeCurrentUser artistUser = FakeCurrentUser.Artist();
        Guid studioId = Guid.NewGuid();
        var artist = new Artist
        {
            StudioId  = studioId,
            UserId    = artistUser.UserId,
            FirstName = "Art",
            LastName  = "Ist",
            Email     = $"{Guid.NewGuid()}@test.com",
        };
        _db.Artists.Add(artist);

        Guid otherRecipientId = Guid.NewGuid();
        _db.NotificationLogs.Add(BuildLog(studioId, artist.Id, NotificationChannel.Email, recipientType: NotificationRecipientType.Artist));
        _db.NotificationLogs.Add(BuildLog(studioId, otherRecipientId, NotificationChannel.Email, recipientType: NotificationRecipientType.Client));
        await _db.SaveChangesAsync();

        GetNotificationsHandler sut = new(_db, artistUser);
        List<NotificationLogResponse> result = await sut.Handle(
            new GetNotificationsQuery(otherRecipientId, null, null, null), default);

        result.Should().ContainSingle(n => n.RecipientId == artist.Id);
    }

    [Fact]
    public async Task Handle_ClientCaller_ReturnsOnlyOwnClientNotifications()
    {
        FakeCurrentUser clientUser = FakeCurrentUser.Client();
        Guid studioId = Guid.NewGuid();
        var client = new Client
        {
            StudioId  = studioId,
            UserId    = clientUser.UserId,
            FirstName = "Cli",
            LastName  = "Ent",
            Email     = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);

        _db.NotificationLogs.Add(BuildLog(studioId, client.Id, NotificationChannel.Email, recipientType: NotificationRecipientType.Client));
        _db.NotificationLogs.Add(BuildLog(studioId, Guid.NewGuid(), NotificationChannel.Email, recipientType: NotificationRecipientType.Client));
        _db.NotificationLogs.Add(BuildLog(studioId, Guid.NewGuid(), NotificationChannel.Email, recipientType: NotificationRecipientType.Artist));
        await _db.SaveChangesAsync();

        GetNotificationsHandler sut = new(_db, clientUser);
        List<NotificationLogResponse> result = await sut.Handle(new GetNotificationsQuery(null, null, null, null), default);

        result.Should().ContainSingle(n => n.RecipientId == client.Id);
    }

    [Fact]
    public async Task Handle_ClientCaller_IgnoresRequestedRecipientIdFilter()
    {
        FakeCurrentUser clientUser = FakeCurrentUser.Client();
        Guid studioId = Guid.NewGuid();
        var client = new Client
        {
            StudioId  = studioId,
            UserId    = clientUser.UserId,
            FirstName = "Cli",
            LastName  = "Ent",
            Email     = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);

        Guid otherRecipientId = Guid.NewGuid();
        _db.NotificationLogs.Add(BuildLog(studioId, client.Id, NotificationChannel.Email, recipientType: NotificationRecipientType.Client));
        _db.NotificationLogs.Add(BuildLog(studioId, otherRecipientId, NotificationChannel.Email, recipientType: NotificationRecipientType.Client));
        await _db.SaveChangesAsync();

        GetNotificationsHandler sut = new(_db, clientUser);
        List<NotificationLogResponse> result = await sut.Handle(
            new GetNotificationsQuery(otherRecipientId, null, null, null), default);

        result.Should().ContainSingle(n => n.RecipientId == client.Id);
    }

    private void SeedLogs(Guid studioId, int count)
    {
        for (int i = 0; i < count; i++)
            _db.NotificationLogs.Add(BuildLog(studioId, Guid.NewGuid(), NotificationChannel.Email));
    }

    private static NotificationLog BuildLog(
        Guid studioId, Guid recipientId, NotificationChannel channel,
        DateTime? sentAt = null,
        NotificationRecipientType recipientType = NotificationRecipientType.Client) => new()
    {
        StudioId      = studioId,
        RecipientId   = recipientId,
        RecipientType = recipientType,
        Channel       = channel,
        Subject       = "Subject",
        Body          = "Body",
        SentAt        = sentAt ?? DateTime.UtcNow,
        IsSuccess     = true
    };
}
