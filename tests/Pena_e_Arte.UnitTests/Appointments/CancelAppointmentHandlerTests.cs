using FluentAssertions;
using MediatR;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class CancelAppointmentHandlerTests
{
    private readonly FakeDbContext       _db       = FakeDbContext.Create();
    private readonly ICurrentTenant      _tenant   = Substitute.For<ICurrentTenant>();
    private readonly IRealtimeNotifier   _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly ISender             _sender   = Substitute.For<ISender>();
    private readonly IJobScheduler       _jobs     = Substitute.For<IJobScheduler>();
    private readonly IStripePaymentService _stripe  = Substitute.For<IStripePaymentService>();
    private readonly Guid                _studioId = Guid.NewGuid();

    public CancelAppointmentHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private CancelAppointmentHandler CreateSut() => new(_db, _tenant, _realtime, _sender, _jobs, _stripe);

    [Fact]
    public async Task Handle_PendingAppointment_SetsStatusToCancelled()
    {
        Guid id = await SeedAppointment(AppointmentStatus.Pending);

        await CreateSut().Handle(new CancelAppointmentCommand(id), default);

        _db.Appointments.Single(a => a.Id == id).Status.Should().Be(AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task Handle_ValidCancel_NotifiesRealtime()
    {
        Guid id = await SeedAppointment(AppointmentStatus.Pending);

        await CreateSut().Handle(new CancelAppointmentCommand(id), default);

        await _realtime.Received(1)
            .NotifyStudioAsync(_studioId, "AppointmentCancelled", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AppointmentNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new CancelAppointmentCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_CompletedAppointment_ThrowsBusinessRuleViolationException()
    {
        Guid id = await SeedAppointment(AppointmentStatus.Completed);

        Func<Task> act = () => CreateSut().Handle(new CancelAppointmentCommand(id), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*cannot be cancelled*");
    }

    [Fact]
    public async Task Handle_CompletedAppointment_DoesNotChangeStatus()
    {
        Guid id = await SeedAppointment(AppointmentStatus.Completed);

        try { await CreateSut().Handle(new CancelAppointmentCommand(id), default); } catch { }

        _db.Appointments.Single(a => a.Id == id).Status.Should().Be(AppointmentStatus.Completed);
    }

    [Fact]
    public async Task Handle_AlreadyCancelledAppointment_IsNoOp()
    {
        Guid id = await SeedAppointment(AppointmentStatus.Cancelled);

        await CreateSut().Handle(new CancelAppointmentCommand(id), default);

        await _sender.DidNotReceive()
            .Send(Arg.Any<SendAppointmentCancellationCommand>(), Arg.Any<CancellationToken>());
        await _realtime.DidNotReceive()
            .NotifyStudioAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCancel_DispatchesCancellationEmail()
    {
        Guid id = await SeedAppointment(AppointmentStatus.Pending);

        await CreateSut().Handle(new CancelAppointmentCommand(id), default);

        await _sender.Received(1)
            .Send(Arg.Is<SendAppointmentCancellationCommand>(c => c.AppointmentId == id),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CompletedAppointment_DoesNotDispatchCancellationEmail()
    {
        Guid id = await SeedAppointment(AppointmentStatus.Completed);

        try { await CreateSut().Handle(new CancelAppointmentCommand(id), default); } catch { }

        await _sender.DidNotReceive()
            .Send(Arg.Any<SendAppointmentCancellationCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PaidCardPayment_RefundsViaStripe()
    {
        Guid id = await SeedAppointment(AppointmentStatus.Confirmed);
        await SeedPayment(id, PaymentStatus.Paid, ClientPaymentMethod.Card, "pi_123");

        await CreateSut().Handle(new CancelAppointmentCommand(id), default);

        await _stripe.Received(1).RefundPaymentIntentAsync("pi_123", null, Arg.Any<CancellationToken>());
        _db.Payments.Single(p => p.AppointmentId == id).Status.Should().Be(PaymentStatus.Refunded);
        _db.Appointments.Single(a => a.Id == id).DepositStatus.Should().Be(DepositStatus.Refunded);
    }

    [Fact]
    public async Task Handle_CapturedCardPayment_RefundsViaStripe()
    {
        Guid id = await SeedAppointment(AppointmentStatus.Confirmed);
        await SeedPayment(id, PaymentStatus.Captured, ClientPaymentMethod.Card, "pi_456");

        await CreateSut().Handle(new CancelAppointmentCommand(id), default);

        await _stripe.Received(1).RefundPaymentIntentAsync("pi_456", null, Arg.Any<CancellationToken>());
        _db.Appointments.Single(a => a.Id == id).DepositStatus.Should().Be(DepositStatus.Refunded);
    }

    [Fact]
    public async Task Handle_PendingCardIntent_DoesNotCallStripeOrMarkRefunded()
    {
        Guid id = await SeedAppointment(AppointmentStatus.Pending);
        await SeedPayment(id, PaymentStatus.Pending, ClientPaymentMethod.Card, "pi_789");

        await CreateSut().Handle(new CancelAppointmentCommand(id), default);

        await _stripe.DidNotReceive().RefundPaymentIntentAsync(
            Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
        _db.Appointments.Single(a => a.Id == id).DepositStatus.Should().Be(DepositStatus.Pending);
    }

    [Fact]
    public async Task Handle_CashPendingPayment_MarksRefundedWithoutStripeCall()
    {
        Guid id = await SeedAppointment(AppointmentStatus.Pending);
        await SeedPayment(id, PaymentStatus.CashPending, ClientPaymentMethod.Cash, null);

        await CreateSut().Handle(new CancelAppointmentCommand(id), default);

        await _stripe.DidNotReceive().RefundPaymentIntentAsync(
            Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
        _db.Payments.Single(p => p.AppointmentId == id).Status.Should().Be(PaymentStatus.Refunded);
        _db.Appointments.Single(a => a.Id == id).DepositStatus.Should().Be(DepositStatus.Refunded);
    }

    private async Task<Guid> SeedAppointment(AppointmentStatus status)
    {
        Appointment appointment = new()
        {
            StudioId        = _studioId,
            ArtistId        = Guid.NewGuid(),
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

    private async Task SeedPayment(
        Guid appointmentId, PaymentStatus status, ClientPaymentMethod method, string? stripeIntentId)
    {
        _db.Payments.Add(new Payment
        {
            StudioId              = _studioId,
            AppointmentId         = appointmentId,
            ClientId              = Guid.NewGuid(),
            Amount                = 50m,
            Status                = status,
            Method                = method,
            StripePaymentIntentId = stripeIntentId,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
