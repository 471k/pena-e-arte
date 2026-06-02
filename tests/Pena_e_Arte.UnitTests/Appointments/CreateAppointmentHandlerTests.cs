using FluentAssertions;
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

public class CreateAppointmentHandlerTests
{
    private readonly FakeDbContext    _db       = FakeDbContext.Create();
    private readonly ICurrentTenant   _tenant   = Substitute.For<ICurrentTenant>();
    private readonly ISlotLocker      _locker   = Substitute.For<ISlotLocker>();
    private readonly IJobScheduler    _jobs     = Substitute.For<IJobScheduler>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly Guid             _studioId = Guid.NewGuid();

    public CreateAppointmentHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _locker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
               .Returns(true);
    }

    private CreateAppointmentHandler CreateSut() =>
        new(_db, _tenant, _locker, _jobs, _realtime);

    [Fact]
    public async Task Handle_ValidRequest_ReturnsAppointmentResponse()
    {
        CreateAppointmentRequest req = ValidRequest();

        AppointmentResponse result = await CreateSut().Handle(new CreateAppointmentCommand(req), default);

        result.ArtistId.Should().Be(req.ArtistId);
        result.ClientId.Should().Be(req.ClientId);
        result.Date.Should().Be(req.Date);
        result.DurationMinutes.Should().Be(req.DurationMinutes);
        result.DepositAmount.Should().Be(req.DepositAmount);
        result.StudioId.Should().Be(_studioId);
        result.Status.Should().Be(AppointmentStatus.Pending.ToString());
    }

    [Fact]
    public async Task Handle_ValidRequest_PersistsAppointmentToDb()
    {
        CreateAppointmentRequest req = ValidRequest();

        await CreateSut().Handle(new CreateAppointmentCommand(req), default);

        _db.Appointments.Should().ContainSingle(a => a.ArtistId == req.ArtistId && a.StudioId == _studioId);
    }

    [Fact]
    public async Task Handle_ValidRequest_SchedulesBothReminders()
    {
        await CreateSut().Handle(new CreateAppointmentCommand(ValidRequest()), default);

        _jobs.Received(1).ScheduleAppointmentReminder(Arg.Any<Guid>(), "48h", Arg.Any<DateTimeOffset>());
        _jobs.Received(1).ScheduleAppointmentReminder(Arg.Any<Guid>(), "24h", Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task Handle_ValidRequest_NotifiesRealtimeWithAppointmentCreated()
    {
        await CreateSut().Handle(new CreateAppointmentCommand(ValidRequest()), default);

        await _realtime.Received(1)
            .NotifyStudioAsync(_studioId, "AppointmentCreated", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LockNotAcquired_ThrowsSlotAlreadyBookedException()
    {
        _locker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
               .Returns(false);

        Func<Task> act = () => CreateSut().Handle(new CreateAppointmentCommand(ValidRequest()), default);

        await act.Should().ThrowAsync<SlotAlreadyBookedException>();
    }

    [Fact]
    public async Task Handle_TimeOverlap_ThrowsSlotAlreadyBookedException()
    {
        CreateAppointmentRequest req = ValidRequest();
        DateTime existingStart = req.Date.AddMinutes(30);
        DateTime existingEnd   = req.Date.AddMinutes(90);

        _db.Appointments.Add(new Appointment
        {
            StudioId        = _studioId,
            ArtistId        = req.ArtistId,
            ClientId        = Guid.NewGuid(),
            Date            = existingStart,
            EndDate         = existingEnd,
            DurationMinutes = 60,
            Status          = AppointmentStatus.Pending,
            DepositStatus   = DepositStatus.Pending
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(new CreateAppointmentCommand(req), default);

        await act.Should().ThrowAsync<SlotAlreadyBookedException>();
    }

    [Fact]
    public async Task Handle_CancelledOverlap_DoesNotThrow()
    {
        CreateAppointmentRequest req = ValidRequest();

        _db.Appointments.Add(new Appointment
        {
            StudioId        = _studioId,
            ArtistId        = req.ArtistId,
            ClientId        = Guid.NewGuid(),
            Date            = req.Date.AddMinutes(30),
            EndDate         = req.Date.AddMinutes(90),
            DurationMinutes = 60,
            Status          = AppointmentStatus.Cancelled,
            DepositStatus   = DepositStatus.Pending
        });
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut().Handle(new CreateAppointmentCommand(req), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_TimeOverlap_StillReleasesLock()
    {
        CreateAppointmentRequest req = ValidRequest();

        _db.Appointments.Add(new Appointment
        {
            StudioId        = _studioId,
            ArtistId        = req.ArtistId,
            ClientId        = Guid.NewGuid(),
            Date            = req.Date.AddMinutes(30),
            EndDate         = req.Date.AddMinutes(90),
            DurationMinutes = 60,
            Status          = AppointmentStatus.Pending,
            DepositStatus   = DepositStatus.Pending
        });
        await _db.SaveChangesAsync();

        try { await CreateSut().Handle(new CreateAppointmentCommand(req), default); } catch { }

        await _locker.Received(1)
            .ReleaseLockAsync(Arg.Any<Guid>(), req.ArtistId, req.Date, Arg.Any<CancellationToken>());
    }

    private static CreateAppointmentRequest ValidRequest() =>
        new(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(3), 90, 50m, null);
}
