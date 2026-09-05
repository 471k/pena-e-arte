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

public class CompleteAppointmentHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IPaymentProvider _paymentProvider = Substitute.For<IPaymentProvider>();
    private readonly Guid _studioId = Guid.NewGuid();

    public CompleteAppointmentHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
    }

    private CompleteAppointmentHandler CreateSut() => new(_db, _tenant, _realtime, _sender, _paymentProvider);

    private Guid SeedAppointment(Guid? artistId, AppointmentStatus status = AppointmentStatus.Confirmed)
    {
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = artistId,
            ClientId = Guid.NewGuid(),
            Date = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(-1).AddMinutes(60),
            DurationMinutes = 60,
            Status = status,
            DepositStatus = DepositStatus.Pending,
        };
        _db.Appointments.Add(appointment);
        _db.SaveChanges();
        return appointment.Id;
    }

    [Fact]
    public async Task Handle_ConfirmedWithArtist_CompletesAndReturnsResponse()
    {
        Guid appointmentId = SeedAppointment(Guid.NewGuid());

        AppointmentResponse result = await CreateSut().Handle(new CompleteAppointmentCommand(appointmentId), default);

        result.Status.Should().Be(AppointmentStatus.Completed.ToString());
    }

    [Fact]
    public async Task Handle_NoArtistAssigned_ThrowsBusinessRuleViolationException()
    {
        Guid appointmentId = SeedAppointment(artistId: null, AppointmentStatus.Pending);

        Func<Task> act = () => CreateSut().Handle(new CompleteAppointmentCommand(appointmentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_NoArtistAssigned_DoesNotChangeStatus()
    {
        Guid appointmentId = SeedAppointment(artistId: null, AppointmentStatus.Pending);

        try { await CreateSut().Handle(new CompleteAppointmentCommand(appointmentId), default); } catch { }

        _db.Appointments.Single(a => a.Id == appointmentId).Status.Should().Be(AppointmentStatus.Pending);
    }

    [Fact]
    public async Task Handle_CancelledAppointment_ThrowsBusinessRuleViolationException()
    {
        Guid appointmentId = SeedAppointment(Guid.NewGuid(), AppointmentStatus.Cancelled);

        Func<Task> act = () => CreateSut().Handle(new CompleteAppointmentCommand(appointmentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_MissingAppointment_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new CompleteAppointmentCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
