using FluentAssertions;
using MediatR;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class AssignAppointmentArtistHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ISlotLocker _locker = Substitute.For<ISlotLocker>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly Guid _studioId = Guid.NewGuid();

    public AssignAppointmentArtistHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _locker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
               .Returns(true);
    }

    private AssignAppointmentArtistHandler CreateSut() =>
        new(_db, _tenant, _locker, _realtime, _sender);

    private Guid SeedArtist(decimal? hourlyRate = null, bool isActive = true)
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            FirstName = "Seed",
            LastName = "Artist",
            Email = $"{Guid.NewGuid()}@artist.test",
            HourlyRate = hourlyRate,
            IsActive = isActive,
        };
        _db.Artists.Add(artist);
        _db.SaveChanges();

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            _db.ArtistSchedules.Add(new ArtistSchedule
            {
                ArtistId = artist.Id,
                StudioId = _studioId,
                DayOfWeek = day,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
                IsAvailable = true,
            });
        }
        _db.SaveChanges();

        return artist.Id;
    }

    private Guid SeedUnassignedAppointment(decimal depositAmount = 0m, DepositStatus depositStatus = DepositStatus.Pending)
    {
        Client client = new()
        {
            StudioId = _studioId,
            FirstName = "Jane",
            LastName = "Doe",
            Email = $"{Guid.NewGuid()}@client.test",
        };
        _db.Clients.Add(client);
        _db.SaveChanges();

        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = null,
            ClientId = client.Id,
            Date = DateTime.UtcNow.AddDays(3),
            EndDate = DateTime.UtcNow.AddDays(3).AddMinutes(90),
            DurationMinutes = 90,
            Status = AppointmentStatus.Pending,
            DepositStatus = depositStatus,
            DepositAmount = depositAmount,
        };
        _db.Appointments.Add(appointment);
        _db.SaveChanges();

        return appointment.Id;
    }

    [Fact]
    public async Task Handle_ValidAssignment_SetsArtistIdAndReturnsResponse()
    {
        Guid artistId = SeedArtist();
        Guid appointmentId = SeedUnassignedAppointment();

        AppointmentResponse result = await CreateSut().Handle(
            new AssignAppointmentArtistCommand(appointmentId, new AssignAppointmentArtistRequest(artistId)), default);

        result.ArtistId.Should().Be(artistId);
        result.ArtistName.Should().Be("Seed Artist");
        _db.Appointments.Should().ContainSingle(a => a.Id == appointmentId && a.ArtistId == artistId);
    }

    [Fact]
    public async Task Handle_ValidAssignment_UpdatesUpdatedAt()
    {
        Guid artistId = SeedArtist();
        Guid appointmentId = SeedUnassignedAppointment();
        Appointment before = _db.Appointments.Single(a => a.Id == appointmentId);
        DateTime originalUpdatedAt = before.UpdatedAt;

        await CreateSut().Handle(
            new AssignAppointmentArtistCommand(appointmentId, new AssignAppointmentArtistRequest(artistId)), default);

        _db.Appointments.Single(a => a.Id == appointmentId).UpdatedAt.Should().BeAfter(originalUpdatedAt);
    }

    [Fact]
    public async Task Handle_DepositAmountZeroAndPending_RecomputesFromArtistRate()
    {
        Guid artistId = SeedArtist(hourlyRate: 100m);
        _db.DepositRules.Add(new DepositRule { StudioId = _studioId, Name = "20%", AmountPercent = 20m, IsActive = true });
        await _db.SaveChangesAsync();
        Guid appointmentId = SeedUnassignedAppointment(depositAmount: 0m, depositStatus: DepositStatus.Pending);

        AppointmentResponse result = await CreateSut().Handle(
            new AssignAppointmentArtistCommand(appointmentId, new AssignAppointmentArtistRequest(artistId)), default);

        // 90 min at €100/h = €150 estimated -> 20% = €30
        result.DepositAmount.Should().Be(30m);
    }

    [Fact]
    public async Task Handle_DepositAmountAlreadyNonzero_DoesNotRecompute()
    {
        Guid artistId = SeedArtist(hourlyRate: 100m);
        _db.DepositRules.Add(new DepositRule { StudioId = _studioId, Name = "Fixed", AmountFixed = 50m, IsActive = true });
        await _db.SaveChangesAsync();
        Guid appointmentId = SeedUnassignedAppointment(depositAmount: 50m, depositStatus: DepositStatus.Pending);

        AppointmentResponse result = await CreateSut().Handle(
            new AssignAppointmentArtistCommand(appointmentId, new AssignAppointmentArtistRequest(artistId)), default);

        result.DepositAmount.Should().Be(50m);
    }

    [Fact]
    public async Task Handle_DepositAlreadyPaid_DoesNotRecomputeEvenIfZero()
    {
        Guid artistId = SeedArtist(hourlyRate: 100m);
        _db.DepositRules.Add(new DepositRule { StudioId = _studioId, Name = "20%", AmountPercent = 20m, IsActive = true });
        await _db.SaveChangesAsync();
        Guid appointmentId = SeedUnassignedAppointment(depositAmount: 0m, depositStatus: DepositStatus.Paid);

        AppointmentResponse result = await CreateSut().Handle(
            new AssignAppointmentArtistCommand(appointmentId, new AssignAppointmentArtistRequest(artistId)), default);

        result.DepositAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_MissingAppointment_ThrowsNotFoundException()
    {
        Guid artistId = SeedArtist();

        Func<Task> act = () => CreateSut().Handle(
            new AssignAppointmentArtistCommand(Guid.NewGuid(), new AssignAppointmentArtistRequest(artistId)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_MissingArtist_ThrowsNotFoundException()
    {
        Guid appointmentId = SeedUnassignedAppointment();

        Func<Task> act = () => CreateSut().Handle(
            new AssignAppointmentArtistCommand(appointmentId, new AssignAppointmentArtistRequest(Guid.NewGuid())), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_InactiveArtist_ThrowsBusinessRuleViolationException()
    {
        Guid artistId = SeedArtist(isActive: false);
        Guid appointmentId = SeedUnassignedAppointment();

        Func<Task> act = () => CreateSut().Handle(
            new AssignAppointmentArtistCommand(appointmentId, new AssignAppointmentArtistRequest(artistId)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    public async Task Handle_TerminalStatusAppointment_ThrowsBusinessRuleViolationException(AppointmentStatus status)
    {
        Guid artistId = SeedArtist();
        Guid appointmentId = SeedUnassignedAppointment();
        Appointment appointment = _db.Appointments.Single(a => a.Id == appointmentId);
        appointment.Status = status;
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(
            new AssignAppointmentArtistCommand(appointmentId, new AssignAppointmentArtistRequest(artistId)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ArtistNotAvailableThatDay_ThrowsBusinessRuleViolationException()
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            FirstName = "No",
            LastName = "Schedule",
            Email = $"{Guid.NewGuid()}@artist.test",
        };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        Guid appointmentId = SeedUnassignedAppointment();

        Func<Task> act = () => CreateSut().Handle(
            new AssignAppointmentArtistCommand(appointmentId, new AssignAppointmentArtistRequest(artist.Id)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ArtistOnTimeOff_ThrowsBusinessRuleViolationException()
    {
        Guid artistId = SeedArtist();
        Guid appointmentId = SeedUnassignedAppointment();
        Appointment appointment = _db.Appointments.Single(a => a.Id == appointmentId);

        _db.ArtistTimeOffs.Add(new ArtistTimeOff
        {
            ArtistId = artistId,
            StudioId = _studioId,
            StartDate = appointment.Date.Date,
            EndDate = appointment.Date.Date,
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(
            new AssignAppointmentArtistCommand(appointmentId, new AssignAppointmentArtistRequest(artistId)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ConflictingAppointmentForArtist_ThrowsSlotAlreadyBookedException()
    {
        Guid artistId = SeedArtist();
        Guid appointmentId = SeedUnassignedAppointment();
        Appointment appointment = _db.Appointments.Single(a => a.Id == appointmentId);

        _db.Appointments.Add(new Appointment
        {
            StudioId = _studioId,
            ArtistId = artistId,
            ClientId = Guid.NewGuid(),
            Date = appointment.Date.AddMinutes(30),
            EndDate = appointment.Date.AddMinutes(90),
            DurationMinutes = 60,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending,
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(
            new AssignAppointmentArtistCommand(appointmentId, new AssignAppointmentArtistRequest(artistId)), default);

        await act.Should().ThrowAsync<SlotAlreadyBookedException>();
    }

    [Fact]
    public async Task Handle_LockNotAcquired_ThrowsSlotAlreadyBookedException()
    {
        Guid artistId = SeedArtist();
        Guid appointmentId = SeedUnassignedAppointment();
        _locker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
               .Returns(false);

        Func<Task> act = () => CreateSut().Handle(
            new AssignAppointmentArtistCommand(appointmentId, new AssignAppointmentArtistRequest(artistId)), default);

        await act.Should().ThrowAsync<SlotAlreadyBookedException>();
    }

    [Fact]
    public async Task Handle_ValidAssignment_ReleasesLock()
    {
        Guid artistId = SeedArtist();
        Guid appointmentId = SeedUnassignedAppointment();

        await CreateSut().Handle(
            new AssignAppointmentArtistCommand(appointmentId, new AssignAppointmentArtistRequest(artistId)), default);

        await _locker.Received(1)
            .ReleaseLockAsync(_studioId, artistId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidAssignment_NotifiesRealtime()
    {
        Guid artistId = SeedArtist();
        Guid appointmentId = SeedUnassignedAppointment();

        await CreateSut().Handle(
            new AssignAppointmentArtistCommand(appointmentId, new AssignAppointmentArtistRequest(artistId)), default);

        await _realtime.Received(1)
            .NotifyStudioAsync(_studioId, "AppointmentArtistAssigned", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidAssignment_SendsArtistAssignedNotification()
    {
        Guid artistId = SeedArtist();
        Guid appointmentId = SeedUnassignedAppointment();

        await CreateSut().Handle(
            new AssignAppointmentArtistCommand(appointmentId, new AssignAppointmentArtistRequest(artistId)), default);

        await _sender.Received(1).Send(
            Arg.Is<SendAppointmentArtistAssignedNotificationCommand>(c => c.AppointmentId == appointmentId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AssignAppointmentArtistCommand_IsAuditableForAppointmentArtistAssigned()
    {
        Guid appointmentId = Guid.NewGuid();
        IAuditableCommand command = new AssignAppointmentArtistCommand(
            appointmentId, new AssignAppointmentArtistRequest(Guid.NewGuid()));

        command.AuditAction.Should().Be("Appointment.ArtistAssigned");
        command.AuditTargetType.Should().Be("Appointment");
        command.AuditTargetId.Should().Be(appointmentId);
    }
}
