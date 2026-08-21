using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Reminders.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Reminders;

public class CancelManualReminderHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly Guid _studioId = Guid.NewGuid();

    public CancelManualReminderHandlerTests()
    {
        _currentUser.Role.Returns("owner");
    }

    private CancelManualReminderHandler CreateSut() => new(_db, _currentUser, _jobs);

    private Guid SeedArtistAsCurrentUser()
    {
        Guid userId = Guid.NewGuid();
        Artist artist = new() { StudioId = _studioId, UserId = userId, FirstName = "Jo", LastName = "Artist", Email = "jo@a.com" };
        _db.Artists.Add(artist);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        _currentUser.Role.Returns("artist");
        _currentUser.UserId.Returns(userId);
        return artist.Id;
    }

    private Guid SeedReminder(Guid artistId, ManualReminderStatus status, string? jobId = "job-1")
    {
        ManualReminder reminder = new()
        {
            StudioId = _studioId, ArtistId = artistId, RecipientName = "Walk-in", RecipientPhone = "+351900000000",
            ScheduledFor = DateTime.UtcNow.AddHours(1), Status = status, JobId = jobId
        };
        _db.ManualReminders.Add(reminder);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return reminder.Id;
    }

    [Fact]
    public async Task Cancel_AlreadySent_ThrowsConflictException()
    {
        Guid artistId = SeedArtistAsCurrentUser();
        Guid id = SeedReminder(artistId, ManualReminderStatus.Sent);

        Func<Task> act = () => CreateSut().Handle(new CancelManualReminderCommand(id), default);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Cancel_AlreadyCancelled_ThrowsConflictException()
    {
        Guid artistId = SeedArtistAsCurrentUser();
        Guid id = SeedReminder(artistId, ManualReminderStatus.Cancelled);

        Func<Task> act = () => CreateSut().Handle(new CancelManualReminderCommand(id), default);

        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Cancel_StillScheduled_DeletesHangfireJobAndSetsCancelled()
    {
        Guid artistId = SeedArtistAsCurrentUser();
        Guid id = SeedReminder(artistId, ManualReminderStatus.Scheduled, jobId: "job-42");

        await CreateSut().Handle(new CancelManualReminderCommand(id), default);

        _jobs.Received(1).CancelJob("job-42");
        _db.ManualReminders.Single(m => m.Id == id).Status.Should().Be(ManualReminderStatus.Cancelled);
    }

    [Fact]
    public async Task Cancel_ArtistNotOwner_ThrowsNotFoundException()
    {
        SeedArtistAsCurrentUser();
        Guid otherArtistId = Guid.NewGuid();
        _db.Artists.Add(new Artist { StudioId = _studioId, Id = otherArtistId, FirstName = "Other", LastName = "Artist", Email = "other@a.com" });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        Guid id = SeedReminder(otherArtistId, ManualReminderStatus.Scheduled);

        Func<Task> act = () => CreateSut().Handle(new CancelManualReminderCommand(id), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Cancel_OwnerRole_BypassesOwnershipCheck()
    {
        Guid artistId = Guid.NewGuid();
        _db.Artists.Add(new Artist { StudioId = _studioId, Id = artistId, FirstName = "Jo", LastName = "Artist", Email = "jo@a.com" });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        Guid id = SeedReminder(artistId, ManualReminderStatus.Scheduled);

        await CreateSut().Handle(new CancelManualReminderCommand(id), default);

        _db.ManualReminders.Single(m => m.Id == id).Status.Should().Be(ManualReminderStatus.Cancelled);
    }

    [Fact]
    public async Task Cancel_NoJobId_DoesNotCallCancelJob()
    {
        Guid artistId = SeedArtistAsCurrentUser();
        Guid id = SeedReminder(artistId, ManualReminderStatus.Scheduled, jobId: null);

        await CreateSut().Handle(new CancelManualReminderCommand(id), default);

        _jobs.DidNotReceive().CancelJob(Arg.Any<string>());
    }
}
