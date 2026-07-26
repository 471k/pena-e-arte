using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class RescheduleAppointmentHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();

    public RescheduleAppointmentHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _currentUser.Role.Returns("artist");
    }

    private RescheduleAppointmentHandler CreateSut() => new(_db, _tenant, _currentUser, _realtime);

    [Fact]
    public async Task Handle_PendingAppointment_UpdatesDateAndDuration()
    {
        Guid id = await SeedAppointment(AppointmentStatus.Pending);
        DateTime newDate = DateTime.UtcNow.AddDays(5);

        var result = await CreateSut().Handle(
            new RescheduleAppointmentCommand(id, new RescheduleAppointmentRequest(newDate, 90, null)), default);

        result.Date.Should().Be(newDate);
        result.DurationMinutes.Should().Be(90);
    }

    [Fact]
    public async Task Handle_ConfirmedAppointment_UpdatesDateAndDuration()
    {
        Guid id = await SeedAppointment(AppointmentStatus.Confirmed);
        DateTime newDate = DateTime.UtcNow.AddDays(3);

        var result = await CreateSut().Handle(
            new RescheduleAppointmentCommand(id, new RescheduleAppointmentRequest(newDate, 60, "moved")), default);

        result.Date.Should().Be(newDate);
    }

    [Fact]
    public async Task Handle_ValidReschedule_NotifiesRealtime()
    {
        Guid id = await SeedAppointment(AppointmentStatus.Pending);

        await CreateSut().Handle(
            new RescheduleAppointmentCommand(id,
                new RescheduleAppointmentRequest(DateTime.UtcNow.AddDays(4), 60, null)), default);

        await _realtime.Received(1)
            .NotifyStudioAsync(_studioId, "AppointmentUpdated", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AppointmentNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new RescheduleAppointmentCommand(Guid.NewGuid(),
                new RescheduleAppointmentRequest(DateTime.UtcNow.AddDays(1), 60, null)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_CancelledAppointment_ThrowsBusinessRuleViolationException()
    {
        Guid id = await SeedAppointment(AppointmentStatus.Cancelled);

        Func<Task> act = () => CreateSut().Handle(
            new RescheduleAppointmentCommand(id,
                new RescheduleAppointmentRequest(DateTime.UtcNow.AddDays(1), 60, null)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*Cancelled*");
    }

    [Fact]
    public async Task Handle_CompletedAppointment_ThrowsBusinessRuleViolationException()
    {
        Guid id = await SeedAppointment(AppointmentStatus.Completed);

        Func<Task> act = () => CreateSut().Handle(
            new RescheduleAppointmentCommand(id,
                new RescheduleAppointmentRequest(DateTime.UtcNow.AddDays(1), 60, null)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*Completed*");
    }

    [Fact]
    public async Task Handle_SlotConflict_ThrowsSlotAlreadyBookedException()
    {
        Guid id = await SeedAppointment(AppointmentStatus.Pending);
        DateTime conflictDate = DateTime.UtcNow.AddDays(10);
        await SeedConflictingAppointment(conflictDate);

        Func<Task> act = () => CreateSut().Handle(
            new RescheduleAppointmentCommand(id,
                new RescheduleAppointmentRequest(conflictDate, 60, null)), default);

        await act.Should().ThrowAsync<SlotAlreadyBookedException>();
    }

    // ── Client self-reschedule ───────────────────────────────────────────────

    private Guid SeedClientAsCurrentUser()
    {
        Guid userId = Guid.NewGuid();
        Client client = new() { StudioId = _studioId, UserId = userId, FirstName = "A", LastName = "B", Email = "a@b.com" };
        _db.Clients.Add(client);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        _currentUser.Role.Returns("client");
        _currentUser.UserId.Returns(userId);
        return client.Id;
    }

    private async Task<Guid> SeedAppointmentForClient(AppointmentStatus status, Guid clientId, DateTime date)
    {
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = _artistId,
            ClientId = clientId,
            Date = date,
            EndDate = date.AddHours(2),
            DurationMinutes = 120,
            Status = status,
            DepositStatus = DepositStatus.Pending
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return appointment.Id;
    }

    [Fact]
    public async Task Handle_ClientReschedulesOutsideNoticeWindow_Succeeds()
    {
        Guid clientId = SeedClientAsCurrentUser();
        Guid id = await SeedAppointmentForClient(AppointmentStatus.Confirmed, clientId, DateTime.UtcNow.AddDays(5));

        var result = await CreateSut().Handle(
            new RescheduleAppointmentCommand(id,
                new RescheduleAppointmentRequest(DateTime.UtcNow.AddDays(6), 60, null)), default);

        result.DurationMinutes.Should().Be(60);
    }

    [Fact]
    public async Task Handle_ClientReschedulesInsideNoticeWindow_ThrowsBusinessRuleViolationException()
    {
        Guid clientId = SeedClientAsCurrentUser();
        // Only 2 hours' notice — inside the 24h platform default window.
        Guid id = await SeedAppointmentForClient(AppointmentStatus.Confirmed, clientId, DateTime.UtcNow.AddHours(2));

        Func<Task> act = () => CreateSut().Handle(
            new RescheduleAppointmentCommand(id,
                new RescheduleAppointmentRequest(DateTime.UtcNow.AddDays(3), 60, null)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*contact the studio directly*");
    }

    [Fact]
    public async Task Handle_ClientReschedulesInsideCustomNoticeWindow_RespectsDepositRule()
    {
        Guid clientId = SeedClientAsCurrentUser();
        _db.DepositRules.Add(new DepositRule
        {
            StudioId = _studioId,
            Name = "Strict",
            AmountFixed = 50m,
            IsActive = true,
            CancellationWindowHours = 72,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // 48 hours' notice — outside the 24h platform default, but inside this studio's 72h rule.
        Guid id = await SeedAppointmentForClient(AppointmentStatus.Confirmed, clientId, DateTime.UtcNow.AddHours(48));

        Func<Task> act = () => CreateSut().Handle(
            new RescheduleAppointmentCommand(id,
                new RescheduleAppointmentRequest(DateTime.UtcNow.AddDays(4), 60, null)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*72 hours*");
    }

    [Fact]
    public async Task Handle_ClientReschedulesAnotherClientsAppointment_ThrowsNotFoundException()
    {
        SeedClientAsCurrentUser();
        Guid id = await SeedAppointmentForClient(AppointmentStatus.Confirmed, Guid.NewGuid(), DateTime.UtcNow.AddDays(5));

        Func<Task> act = () => CreateSut().Handle(
            new RescheduleAppointmentCommand(id,
                new RescheduleAppointmentRequest(DateTime.UtcNow.AddDays(6), 60, null)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_StaffReschedulesInsideClientNoticeWindow_StillSucceeds()
    {
        // Regression: staff reschedule must be completely unaffected by the client
        // notice-window check, even for an imminent appointment.
        Guid id = await SeedAppointment(AppointmentStatus.Confirmed, DateTime.UtcNow.AddHours(1));

        var result = await CreateSut().Handle(
            new RescheduleAppointmentCommand(id,
                new RescheduleAppointmentRequest(DateTime.UtcNow.AddHours(3), 60, null)), default);

        result.DurationMinutes.Should().Be(60);
    }

    private Task<Guid> SeedAppointment(AppointmentStatus status) =>
        SeedAppointment(status, DateTime.UtcNow.AddDays(1));

    private async Task<Guid> SeedAppointment(AppointmentStatus status, DateTime date)
    {
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = _artistId,
            ClientId = Guid.NewGuid(),
            Date = date,
            EndDate = date.AddHours(2),
            DurationMinutes = 120,
            Status = status,
            DepositStatus = DepositStatus.Pending
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return appointment.Id;
    }

    private async Task SeedConflictingAppointment(DateTime date)
    {
        _db.Appointments.Add(new Appointment
        {
            StudioId = _studioId,
            ArtistId = _artistId,
            ClientId = Guid.NewGuid(),
            Date = date,
            EndDate = date.AddHours(2),
            DurationMinutes = 120,
            Status = AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Pending
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
