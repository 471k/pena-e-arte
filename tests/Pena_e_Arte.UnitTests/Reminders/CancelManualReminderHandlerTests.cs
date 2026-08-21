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
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly FakeDbContext _db;
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly Guid _studioId = Guid.NewGuid();

    public CancelManualReminderHandlerTests()
    {
        _db = FakeDbContext.Create(_dbName);
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
            StudioId = _studioId,
            ArtistId = artistId,
            RecipientName = "Walk-in",
            RecipientPhone = "+351900000000",
            ScheduledFor = DateTime.UtcNow.AddHours(1),
            Status = status,
            JobId = jobId
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

    [Fact]
    public async Task Cancel_LinkedArtistNoLongerResolves_ThrowsNotFoundException()
    {
        // No corresponding Artist row for this ArtistId — mirrors what a soft-deleted artist
        // looks like in production (excluded by Artist's global query filter, which
        // FakeDbContext doesn't replicate; a missing related row produces the same null
        // navigation via .Include(), which is what the handler actually branches on).
        _currentUser.Role.Returns("artist");
        _currentUser.UserId.Returns(Guid.NewGuid());
        Guid id = SeedReminder(Guid.NewGuid(), ManualReminderStatus.Scheduled);

        Func<Task> act = () => CreateSut().Handle(new CancelManualReminderCommand(id), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Cancel_ReminderAlreadySentByJobConcurrently_ThrowsConflictException()
    {
        Guid artistId = SeedArtistAsCurrentUser();
        Guid id = SeedReminder(artistId, ManualReminderStatus.Scheduled, jobId: "job-1");

        // Simulates ManualReminderJob concurrently transitioning this same reminder to Sent —
        // a second FakeDbContext against the same in-memory database, so its committed write
        // is visible to this handler's own AsNoTracking re-check (unlike a second query on
        // the SAME context, which would just return the already-tracked, stale instance).
        using (FakeDbContext otherDb = FakeDbContext.Create(_dbName))
        {
            ManualReminder reminder = otherDb.ManualReminders.Single(m => m.Id == id);
            reminder.Status = ManualReminderStatus.Sent;
            reminder.SentAt = DateTime.UtcNow;
            await otherDb.SaveChangesAsync();
        }

        Func<Task> act = () => CreateSut().Handle(new CancelManualReminderCommand(id), default);

        await act.Should().ThrowAsync<ConflictException>();
    }
}
