using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Reminders.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Reminders;

public class GetManualRemindersHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _studioId = Guid.NewGuid();

    public GetManualRemindersHandlerTests()
    {
        _currentUser.Role.Returns("owner");
    }

    private GetManualRemindersHandler CreateSut() => new(_db, _currentUser);

    private Guid SeedReminder(Guid? appointmentId, Guid? clientId, Guid? artistId = null)
    {
        Guid resolvedArtistId = artistId ?? Guid.NewGuid();
        if (artistId is null)
        {
            _db.Artists.Add(new Artist { StudioId = _studioId, Id = resolvedArtistId, FirstName = "Jo", LastName = "Artist", Email = $"{Guid.NewGuid()}@a.com" });
        }
        ManualReminder reminder = new()
        {
            StudioId = _studioId,
            ArtistId = resolvedArtistId,
            AppointmentId = appointmentId,
            ClientId = clientId,
            RecipientName = "Walk-in",
            RecipientPhone = "+351900000000",
            ScheduledFor = DateTime.UtcNow.AddHours(1),
            Status = ManualReminderStatus.Scheduled
        };
        _db.ManualReminders.Add(reminder);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return reminder.Id;
    }

    private Guid SeedArtistAsCurrentUser()
    {
        Guid userId = Guid.NewGuid();
        Artist artist = new() { StudioId = _studioId, UserId = userId, FirstName = "Jo", LastName = "Artist", Email = $"{Guid.NewGuid()}@a.com" };
        _db.Artists.Add(artist);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        _currentUser.Role.Returns("artist");
        _currentUser.UserId.Returns(userId);
        return artist.Id;
    }

    // -- Quick reminders (raw contact: no appointment, no client) ------------------------------

    [Fact]
    public async Task Handle_QuickOnly_ReturnsOnlyUnlinkedReminders()
    {
        Guid quick = SeedReminder(null, null);
        SeedReminder(Guid.NewGuid(), null);
        SeedReminder(null, Guid.NewGuid());

        List<ManualReminderResponse> result =
            await CreateSut().Handle(new GetManualRemindersQuery(null, null, QuickOnly: true), default);

        result.Select(r => r.Id).Should().Equal(quick);
    }

    [Fact]
    public async Task Handle_QuickOnly_ArtistCaller_SeesOnlyTheirOwn()
    {
        Guid myArtistId = SeedArtistAsCurrentUser();
        Guid mine = SeedReminder(null, null, artistId: myArtistId);
        SeedReminder(null, null);

        List<ManualReminderResponse> result =
            await CreateSut().Handle(new GetManualRemindersQuery(null, null, QuickOnly: true), default);

        result.Select(r => r.Id).Should().Equal(mine);
    }

    [Fact]
    public async Task Handle_QuickOnly_ArtistCaller_IgnoresAnArtistIdFilterForAnotherArtist()
    {
        Guid myArtistId = SeedArtistAsCurrentUser();
        Guid mine = SeedReminder(null, null, artistId: myArtistId);
        Guid colleague = SeedReminder(null, null);
        Guid colleagueArtistId = _db.ManualReminders.Single(m => m.Id == colleague).ArtistId;

        List<ManualReminderResponse> result = await CreateSut().Handle(
            new GetManualRemindersQuery(null, null, QuickOnly: true, ArtistId: colleagueArtistId), default);

        // A colleague's id must not widen an artist's scope.
        result.Select(r => r.Id).Should().Equal(mine);
    }

    [Fact]
    public async Task Handle_QuickOnly_OwnerCaller_SeesAllAndCanFilterByArtist()
    {
        Guid first = SeedReminder(null, null);
        SeedReminder(null, null);
        Guid firstArtistId = _db.ManualReminders.Single(m => m.Id == first).ArtistId;

        List<ManualReminderResponse> all =
            await CreateSut().Handle(new GetManualRemindersQuery(null, null, QuickOnly: true), default);
        List<ManualReminderResponse> filtered = await CreateSut().Handle(
            new GetManualRemindersQuery(null, null, QuickOnly: true, ArtistId: firstArtistId), default);

        all.Should().HaveCount(2);
        filtered.Select(r => r.Id).Should().Equal(first);
    }

    [Fact]
    public async Task Handle_QuickOnly_ReturnsNewestFirstAndCapsTheList()
    {
        Guid artistId = Guid.NewGuid();
        _db.Artists.Add(new Artist { StudioId = _studioId, Id = artistId, FirstName = "Jo", LastName = "Artist", Email = "jo@a.com" });
        for (int i = 0; i < GetManualRemindersQuery.QuickReminderLimit + 5; i++)
        {
            _db.ManualReminders.Add(new ManualReminder
            {
                StudioId = _studioId,
                ArtistId = artistId,
                RecipientName = $"R{i}",
                RecipientPhone = "+355690000000",
                ScheduledFor = DateTime.UtcNow.AddMinutes(i),
                Status = ManualReminderStatus.Sent,
            });
        }
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        List<ManualReminderResponse> result =
            await CreateSut().Handle(new GetManualRemindersQuery(null, null, QuickOnly: true), default);

        result.Should().HaveCount(GetManualRemindersQuery.QuickReminderLimit);
        result.Select(r => r.ScheduledFor).Should().BeInDescendingOrder();
    }

    [Fact]
    public async Task Handle_QuickOnlyCombinedWithAnAppointmentFilter_ThrowsBusinessRuleViolationException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new GetManualRemindersQuery(Guid.NewGuid(), null, QuickOnly: true), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_NoFilterProvided_ThrowsBusinessRuleViolationException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetManualRemindersQuery(null, null), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_FilterByAppointmentId_ReturnsMatchingReminders()
    {
        Guid appointmentId = Guid.NewGuid();
        Guid id = SeedReminder(appointmentId, null);
        SeedReminder(Guid.NewGuid(), null);

        var result = await CreateSut().Handle(new GetManualRemindersQuery(appointmentId, null), default);

        result.Should().ContainSingle(r => r.Id == id);
    }

    [Fact]
    public async Task Handle_FailedReminder_ReturnsItsFailureReasonAndOthersReturnNull()
    {
        Guid clientId = Guid.NewGuid();
        Guid failedId = SeedReminder(null, clientId);
        Guid scheduledId = SeedReminder(null, clientId);
        ManualReminder failed = _db.ManualReminders.Single(m => m.Id == failedId);
        failed.Status = ManualReminderStatus.Failed;
        failed.FailureReason = ManualReminderFailureReason.PlanLimit;
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var result = await CreateSut().Handle(new GetManualRemindersQuery(null, clientId), default);

        result.Single(r => r.Id == failedId).FailureReason.Should().Be("PlanLimit");
        result.Single(r => r.Id == scheduledId).FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task Handle_FilterByClientId_ReturnsMatchingReminders()
    {
        Guid clientId = Guid.NewGuid();
        Guid id = SeedReminder(null, clientId);
        SeedReminder(null, Guid.NewGuid());

        var result = await CreateSut().Handle(new GetManualRemindersQuery(null, clientId), default);

        result.Should().ContainSingle(r => r.Id == id);
    }

    [Fact]
    public async Task Handle_ArtistCaller_DoesNotSeeColleaguesRemindersForSameAppointment()
    {
        Guid appointmentId = Guid.NewGuid();
        Guid myArtistId = SeedArtistAsCurrentUser();
        // A colleague's reminder for the SAME appointment — must never be visible to me.
        SeedReminder(appointmentId, null, artistId: Guid.NewGuid());
        Guid myReminderId = SeedReminder(appointmentId, null, artistId: myArtistId);

        var result = await CreateSut().Handle(new GetManualRemindersQuery(appointmentId, null), default);

        result.Should().ContainSingle(r => r.Id == myReminderId);
    }

    [Fact]
    public async Task Handle_ArtistCaller_DoesNotSeeColleaguesRemindersForSameClient()
    {
        Guid clientId = Guid.NewGuid();
        Guid myArtistId = SeedArtistAsCurrentUser();
        SeedReminder(null, clientId, artistId: Guid.NewGuid());
        Guid myReminderId = SeedReminder(null, clientId, artistId: myArtistId);

        var result = await CreateSut().Handle(new GetManualRemindersQuery(null, clientId), default);

        result.Should().ContainSingle(r => r.Id == myReminderId);
    }

    [Fact]
    public async Task Handle_OwnerCaller_SeesAllArtistsReminders()
    {
        Guid clientId = Guid.NewGuid();
        Guid firstId = SeedReminder(null, clientId, artistId: Guid.NewGuid());
        Guid secondId = SeedReminder(null, clientId, artistId: Guid.NewGuid());

        var result = await CreateSut().Handle(new GetManualRemindersQuery(null, clientId), default);

        result.Select(r => r.Id).Should().BeEquivalentTo([firstId, secondId]);
    }
}
