using FluentAssertions;
using MediatR;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class ConfirmAppointmentHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly Guid _studioId = Guid.NewGuid();

    public ConfirmAppointmentHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
    }

    private ConfirmAppointmentHandler CreateSut() => new(_db, _tenant, _realtime, _sender);

    private Guid SeedAppointment(Guid? artistId, AppointmentStatus status = AppointmentStatus.Pending)
    {
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = artistId,
            ClientId = Guid.NewGuid(),
            Date = DateTime.UtcNow.AddDays(3),
            EndDate = DateTime.UtcNow.AddDays(3).AddMinutes(60),
            DurationMinutes = 60,
            Status = status,
            DepositStatus = DepositStatus.Pending,
        };
        _db.Appointments.Add(appointment);
        _db.SaveChanges();
        return appointment.Id;
    }

    [Fact]
    public async Task Handle_PendingWithArtist_ConfirmsAndReturnsResponse()
    {
        Guid appointmentId = SeedAppointment(Guid.NewGuid());

        AppointmentResponse result = await CreateSut().Handle(new ConfirmAppointmentCommand(appointmentId), default);

        result.Status.Should().Be(AppointmentStatus.Confirmed.ToString());
    }

    [Fact]
    public async Task Handle_NoArtistAssigned_ThrowsBusinessRuleViolationException()
    {
        Guid appointmentId = SeedAppointment(artistId: null);

        Func<Task> act = () => CreateSut().Handle(new ConfirmAppointmentCommand(appointmentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_NoArtistAssigned_DoesNotChangeStatus()
    {
        Guid appointmentId = SeedAppointment(artistId: null);

        try { await CreateSut().Handle(new ConfirmAppointmentCommand(appointmentId), default); } catch { }

        _db.Appointments.Single(a => a.Id == appointmentId).Status.Should().Be(AppointmentStatus.Pending);
    }

    [Fact]
    public async Task Handle_NotPending_ThrowsBusinessRuleViolationException()
    {
        Guid appointmentId = SeedAppointment(Guid.NewGuid(), AppointmentStatus.Confirmed);

        Func<Task> act = () => CreateSut().Handle(new ConfirmAppointmentCommand(appointmentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_MissingAppointment_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new ConfirmAppointmentCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
