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
    private readonly FakeDbContext     _db       = FakeDbContext.Create();
    private readonly ICurrentTenant    _tenant   = Substitute.For<ICurrentTenant>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly Guid              _studioId = Guid.NewGuid();
    private readonly Guid              _artistId = Guid.NewGuid();

    public RescheduleAppointmentHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private RescheduleAppointmentHandler CreateSut() => new(_db, _tenant, _realtime);

    [Fact]
    public async Task Handle_PendingAppointment_UpdatesDateAndDuration()
    {
        Guid     id      = await SeedAppointment(AppointmentStatus.Pending);
        DateTime newDate = DateTime.UtcNow.AddDays(5);

        var result = await CreateSut().Handle(
            new RescheduleAppointmentCommand(id, new RescheduleAppointmentRequest(newDate, 90, null)), default);

        result.Date.Should().Be(newDate);
        result.DurationMinutes.Should().Be(90);
    }

    [Fact]
    public async Task Handle_ConfirmedAppointment_UpdatesDateAndDuration()
    {
        Guid     id      = await SeedAppointment(AppointmentStatus.Confirmed);
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
        Guid     id          = await SeedAppointment(AppointmentStatus.Pending);
        DateTime conflictDate = DateTime.UtcNow.AddDays(10);
        await SeedConflictingAppointment(conflictDate);

        Func<Task> act = () => CreateSut().Handle(
            new RescheduleAppointmentCommand(id,
                new RescheduleAppointmentRequest(conflictDate, 60, null)), default);

        await act.Should().ThrowAsync<SlotAlreadyBookedException>();
    }

    private async Task<Guid> SeedAppointment(AppointmentStatus status)
    {
        Appointment appointment = new()
        {
            StudioId        = _studioId,
            ArtistId        = _artistId,
            ClientId        = Guid.NewGuid(),
            Date            = DateTime.UtcNow.AddDays(1),
            EndDate         = DateTime.UtcNow.AddDays(1).AddHours(2),
            DurationMinutes = 120,
            Status          = status,
            DepositStatus   = DepositStatus.Pending
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
            StudioId        = _studioId,
            ArtistId        = _artistId,
            ClientId        = Guid.NewGuid(),
            Date            = date,
            EndDate         = date.AddHours(2),
            DurationMinutes = 120,
            Status          = AppointmentStatus.Confirmed,
            DepositStatus   = DepositStatus.Pending
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
