using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class MarkNoShowHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly IPaymentProvider _paymentProvider = Substitute.For<IPaymentProvider>();
    private readonly Guid _studioId = Guid.NewGuid();

    public MarkNoShowHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
    }

    private MarkNoShowHandler CreateSut() => new(_db, _tenant, _realtime, _paymentProvider);

    private Guid SeedAppointment(AppointmentStatus status = AppointmentStatus.Confirmed)
    {
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = Guid.NewGuid(),
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
    public async Task Handle_HeldCardDeposit_CapturesAndSetsPaidAt()
    {
        // Revenue reports filter on PaidAt != null: a no-show deposit the studio collects must
        // carry PaidAt. The deposit itself stays Forfeited (intended).
        Guid appointmentId = SeedAppointment();
        Payment payment = new()
        {
            StudioId = _studioId,
            AppointmentId = appointmentId,
            ClientId = Guid.NewGuid(),
            Amount = 1234m,
            Currency = "ALL",
            Status = PaymentStatus.Captured,
            Method = ClientPaymentMethod.Card,
            ProviderReferenceId = "pok_held",
        };
        _db.Payments.Add(payment);
        _db.SaveChanges();

        await CreateSut().Handle(new MarkNoShowCommand(appointmentId), default);

        await _paymentProvider.Received(1).CaptureAsync(_studioId, "pok_held", Arg.Any<CancellationToken>());
        Payment stored = _db.Payments.Single(p => p.Id == payment.Id);
        stored.Status.Should().Be(PaymentStatus.Paid);
        stored.PaidAt.Should().NotBeNull();
        Appointment appointment = _db.Appointments.Single(a => a.Id == appointmentId);
        appointment.Status.Should().Be(AppointmentStatus.NoShow);
        appointment.DepositStatus.Should().Be(DepositStatus.Forfeited);
    }

    [Fact]
    public async Task Handle_CancelledAppointment_Throws()
    {
        Guid appointmentId = SeedAppointment(AppointmentStatus.Cancelled);

        Func<Task> act = () => CreateSut().Handle(new MarkNoShowCommand(appointmentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
        await _paymentProvider.DidNotReceive().CaptureAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
