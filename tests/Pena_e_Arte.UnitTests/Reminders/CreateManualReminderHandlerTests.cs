using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.Reminders.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Reminders;

public class CreateManualReminderHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly IManualReminderQuotaService _quota = Substitute.For<IManualReminderQuotaService>();
    private readonly Guid _studioId = Guid.NewGuid();

    public CreateManualReminderHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _jobs.ScheduleManualReminder(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>()).Returns("hangfire-job-1");
    }

    private CreateManualReminderHandler CreateSut() => new(_db, _tenant, _currentUser, _jobs, _quota);

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

    private Guid SeedClient(Guid artistId, string? phone = "+351910000001", bool optOut = false)
    {
        Client client = new()
        {
            StudioId = _studioId,
            ArtistId = artistId,
            FirstName = "Ana",
            LastName = "Silva",
            Email = $"{Guid.NewGuid()}@c.com",
            Phone = phone,
            SmsOptOut = optOut
        };
        _db.Clients.Add(client);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return client.Id;
    }

    private Guid SeedAppointment(Guid artistId, Guid clientId)
    {
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = artistId,
            ClientId = clientId,
            Date = DateTime.UtcNow.AddDays(2),
            EndDate = DateTime.UtcNow.AddDays(2).AddHours(2),
            DurationMinutes = 120,
            Status = AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Paid
        };
        _db.Appointments.Add(appointment);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return appointment.Id;
    }

    [Fact]
    public async Task Handle_AppointmentLinked_ResolvesRecipientFromClient_ReturnsScheduledReminder()
    {
        Guid artistId = SeedArtistAsCurrentUser();
        Guid clientId = SeedClient(artistId);
        Guid appointmentId = SeedAppointment(artistId, clientId);

        CreateManualReminderRequest req = new(appointmentId, null, null, null, null, null, null);
        ManualReminderResponse result = await CreateSut().Handle(new CreateManualReminderCommand(req), default);

        result.RecipientName.Should().Be("Ana Silva");
        result.RecipientPhone.Should().Be("+351910000001");
        result.AppointmentId.Should().Be(appointmentId);
        result.ClientId.Should().Be(clientId);
        result.Status.Should().Be(ManualReminderStatus.Scheduled.ToString());
    }

    [Fact]
    public async Task Handle_ClientLinked_ResolvesRecipientFromClient_ReturnsScheduledReminder()
    {
        Guid artistId = SeedArtistAsCurrentUser();
        Guid clientId = SeedClient(artistId);

        CreateManualReminderRequest req = new(null, clientId, null, null, null, null, null);
        ManualReminderResponse result = await CreateSut().Handle(new CreateManualReminderCommand(req), default);

        result.RecipientName.Should().Be("Ana Silva");
        result.ClientId.Should().Be(clientId);
        result.AppointmentId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_RawContact_DoesNotCreateClientRow_UsesTypedNameAndPhone()
    {
        SeedArtistAsCurrentUser();
        int clientsBefore = _db.Clients.Count();

        CreateManualReminderRequest req = new(null, null, null, "Walk-in Wendy", "+351920000002", null, null);
        ManualReminderResponse result = await CreateSut().Handle(new CreateManualReminderCommand(req), default);

        result.RecipientName.Should().Be("Walk-in Wendy");
        result.RecipientPhone.Should().Be("+351920000002");
        result.ClientId.Should().BeNull();
        _db.Clients.Count().Should().Be(clientsBefore);
    }

    [Fact]
    public async Task Handle_AppointmentClientHasNoPhone_ThrowsBusinessRuleViolationException()
    {
        Guid artistId = SeedArtistAsCurrentUser();
        Guid clientId = SeedClient(artistId, phone: null);
        Guid appointmentId = SeedAppointment(artistId, clientId);

        CreateManualReminderRequest req = new(appointmentId, null, null, null, null, null, null);
        Func<Task> act = () => CreateSut().Handle(new CreateManualReminderCommand(req), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ClientOptedOut_ThrowsBusinessRuleViolationException()
    {
        Guid artistId = SeedArtistAsCurrentUser();
        Guid clientId = SeedClient(artistId, optOut: true);

        CreateManualReminderRequest req = new(null, clientId, null, null, null, null, null);
        Func<Task> act = () => CreateSut().Handle(new CreateManualReminderCommand(req), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ArtistNotOwnerOfAppointment_ThrowsNotFoundException()
    {
        SeedArtistAsCurrentUser();
        Guid otherArtistId = Guid.NewGuid();
        _db.Artists.Add(new Artist { StudioId = _studioId, Id = otherArtistId, FirstName = "Other", LastName = "Artist", Email = "other@a.com" });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        Guid clientId = SeedClient(otherArtistId);
        Guid appointmentId = SeedAppointment(otherArtistId, clientId);

        CreateManualReminderRequest req = new(appointmentId, null, null, null, null, null, null);
        Func<Task> act = () => CreateSut().Handle(new CreateManualReminderCommand(req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ArtistNotOwnerOfClient_ThrowsNotFoundException()
    {
        SeedArtistAsCurrentUser();
        Guid otherArtistId = Guid.NewGuid();
        Guid clientId = SeedClient(otherArtistId);

        CreateManualReminderRequest req = new(null, clientId, null, null, null, null, null);
        Func<Task> act = () => CreateSut().Handle(new CreateManualReminderCommand(req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_OwnerRole_RequiresArtistIdOnRequest_ThrowsWhenMissing()
    {
        _tenant.StudioId.Returns(_studioId);
        _currentUser.Role.Returns("owner");

        CreateManualReminderRequest req = new(null, null, null, "Walk-in", "+351900000000", null, null);
        Func<Task> act = () => CreateSut().Handle(new CreateManualReminderCommand(req), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_OwnerRole_ActsOnBehalfOfSpecifiedArtist()
    {
        _currentUser.Role.Returns("owner");
        Guid artistId = Guid.NewGuid();
        _db.Artists.Add(new Artist { StudioId = _studioId, Id = artistId, FirstName = "Jo", LastName = "Artist", Email = "jo@a.com" });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        CreateManualReminderRequest req = new(null, null, artistId, "Walk-in", "+351900000000", null, null);
        await CreateSut().Handle(new CreateManualReminderCommand(req), default);

        _db.ManualReminders.Single().ArtistId.Should().Be(artistId);
    }

    [Fact]
    public async Task Handle_ScheduledForOmitted_SchedulesImmediateSend()
    {
        SeedArtistAsCurrentUser();

        CreateManualReminderRequest req = new(null, null, null, "Walk-in", "+351900000000", null, null);
        await CreateSut().Handle(new CreateManualReminderCommand(req), default);

        _jobs.Received(1).ScheduleManualReminder(
            Arg.Any<Guid>(), Arg.Is<DateTimeOffset>(d => d <= DateTimeOffset.UtcNow.AddSeconds(1)));
    }

    [Fact]
    public async Task Handle_ScheduledForFuture_SchedulesViaHangfireAtGivenTime()
    {
        SeedArtistAsCurrentUser();
        DateTime scheduledFor = DateTime.UtcNow.AddDays(1);

        CreateManualReminderRequest req = new(null, null, null, "Walk-in", "+351900000000", null, scheduledFor);
        await CreateSut().Handle(new CreateManualReminderCommand(req), default);

        _jobs.Received(1).ScheduleManualReminder(
            Arg.Any<Guid>(), Arg.Is<DateTimeOffset>(d => d.UtcDateTime == scheduledFor));
    }

    [Fact]
    public async Task Handle_QuotaExceeded_PropagatesManualReminderQuotaExceededException()
    {
        SeedArtistAsCurrentUser();
        _quota.CheckAndIncrementAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
              .ThrowsAsync(new ManualReminderQuotaExceededException());

        CreateManualReminderRequest req = new(null, null, null, "Walk-in", "+351900000000", null, null);
        Func<Task> act = () => CreateSut().Handle(new CreateManualReminderCommand(req), default);

        await act.Should().ThrowAsync<ManualReminderQuotaExceededException>();
        _db.ManualReminders.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ValidCreate_SetsAuditTargetIdToNewReminderId()
    {
        SeedArtistAsCurrentUser();

        CreateManualReminderRequest req = new(null, null, null, "Walk-in", "+351900000000", null, null);
        CreateManualReminderCommand command = new(req);
        ManualReminderResponse result = await CreateSut().Handle(command, default);

        command.AuditTargetId.Should().Be(result.Id);
    }
}
