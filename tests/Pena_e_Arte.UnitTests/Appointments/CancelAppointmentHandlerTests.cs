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
    private readonly FakeDbContext       _db          = FakeDbContext.Create();
    private readonly ICurrentTenant      _tenant      = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser        _currentUser = Substitute.For<ICurrentUser>();
    private readonly IRealtimeNotifier   _realtime    = Substitute.For<IRealtimeNotifier>();
    private readonly ISender             _sender      = Substitute.For<ISender>();
    private readonly IJobScheduler       _jobs        = Substitute.For<IJobScheduler>();
    private readonly IStripePaymentService _stripe    = Substitute.For<IStripePaymentService>();
    private readonly Guid                _studioId    = Guid.NewGuid();

    public CancelAppointmentHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _currentUser.Role.Returns("artist");
    }

    private CancelAppointmentHandler CreateSut() =>
        new(_db, _tenant, _currentUser, _realtime, _sender, _jobs, _stripe);

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

    // ── Client self-cancel ───────────────────────────────────────────────────

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

    private async Task<Guid> SeedAppointmentForClient(
        AppointmentStatus status, Guid clientId, DateTime date, decimal depositAmount = 50m)
    {
        Appointment appointment = new()
        {
            StudioId        = _studioId,
            ArtistId        = Guid.NewGuid(),
            ClientId        = clientId,
            Date            = date,
            EndDate         = date.AddHours(2),
            DurationMinutes = 120,
            Status          = status,
            DepositStatus   = DepositStatus.Pending,
            DepositAmount   = depositAmount
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return appointment.Id;
    }

    [Fact]
    public async Task Handle_ClientCancelsOwnAppointment_Succeeds()
    {
        Guid clientId = SeedClientAsCurrentUser();
        Guid id = await SeedAppointmentForClient(AppointmentStatus.Pending, clientId, DateTime.UtcNow.AddDays(5));

        await CreateSut().Handle(new CancelAppointmentCommand(id), default);

        _db.Appointments.Single(a => a.Id == id).Status.Should().Be(AppointmentStatus.Cancelled);
        _db.Appointments.Single(a => a.Id == id).CancellationReason.Should().Be(CancellationReason.ClientCancelled);
    }

    [Fact]
    public async Task Handle_ClientCancelsAnotherClientsAppointment_ThrowsNotFoundException()
    {
        SeedClientAsCurrentUser();
        Guid id = await SeedAppointmentForClient(AppointmentStatus.Pending, Guid.NewGuid(), DateTime.UtcNow.AddDays(5));

        Func<Task> act = () => CreateSut().Handle(new CancelAppointmentCommand(id), default);

        await act.Should().ThrowAsync<NotFoundException>();
        _db.Appointments.Single(a => a.Id == id).Status.Should().Be(AppointmentStatus.Pending);
    }

    [Fact]
    public async Task Handle_ClientCancelsCompletedAppointment_ThrowsBusinessRuleViolationException()
    {
        Guid clientId = SeedClientAsCurrentUser();
        Guid id = await SeedAppointmentForClient(AppointmentStatus.Completed, clientId, DateTime.UtcNow.AddDays(-1));

        Func<Task> act = () => CreateSut().Handle(new CancelAppointmentCommand(id), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ClientCancelsNoShowAppointment_ThrowsBusinessRuleViolationException()
    {
        Guid clientId = SeedClientAsCurrentUser();
        Guid id = await SeedAppointmentForClient(AppointmentStatus.NoShow, clientId, DateTime.UtcNow.AddDays(-1));

        Func<Task> act = () => CreateSut().Handle(new CancelAppointmentCommand(id), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_ClientCancelsOutsideNoticeWindow_RefundsFully()
    {
        Guid clientId = SeedClientAsCurrentUser();
        Guid id = await SeedAppointmentForClient(AppointmentStatus.Confirmed, clientId, DateTime.UtcNow.AddDays(5));
        await SeedPayment(id, PaymentStatus.Paid, ClientPaymentMethod.Card, "pi_client_1");

        await CreateSut().Handle(new CancelAppointmentCommand(id), default);

        await _stripe.Received(1).RefundPaymentIntentAsync("pi_client_1", null, Arg.Any<CancellationToken>());
        _db.Appointments.Single(a => a.Id == id).DepositStatus.Should().Be(DepositStatus.Refunded);
    }

    [Fact]
    public async Task Handle_ClientCancelsInsideNoticeWindow_ForfeitsDepositByDefault()
    {
        Guid clientId = SeedClientAsCurrentUser();
        // Only 2 hours' notice — inside the 24h platform default window, no DepositRule configured.
        Guid id = await SeedAppointmentForClient(AppointmentStatus.Confirmed, clientId, DateTime.UtcNow.AddHours(2));
        await SeedPayment(id, PaymentStatus.Paid, ClientPaymentMethod.Card, "pi_client_2");

        await CreateSut().Handle(new CancelAppointmentCommand(id), default);

        await _stripe.DidNotReceive().RefundPaymentIntentAsync(
            Arg.Any<string>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
        _db.Appointments.Single(a => a.Id == id).DepositStatus.Should().Be(DepositStatus.Forfeited);
        _db.Payments.Single(p => p.AppointmentId == id).Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public async Task Handle_ClientCancelsCashPendingDepositInsideNoticeWindow_NeverForfeits()
    {
        // CashPending means nothing has actually been collected yet (the client only
        // declared intent to pay cash) — so even a studio configured for 0% late-cancel
        // refund has nothing to forfeit here. This is intentional, not a policy bypass:
        // it mirrors how an unauthorized/never-captured card payment is also a no-op.
        Guid clientId = SeedClientAsCurrentUser();
        _db.DepositRules.Add(new DepositRule
        {
            StudioId = _studioId, Name = "Strict", AmountFixed = 50m, IsActive = true,
            CancellationWindowHours = 24, RefundPercentOnLateCancel = 0,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Guid id = await SeedAppointmentForClient(AppointmentStatus.Confirmed, clientId, DateTime.UtcNow.AddHours(2));
        await SeedPayment(id, PaymentStatus.CashPending, ClientPaymentMethod.Cash, null);

        await CreateSut().Handle(new CancelAppointmentCommand(id), default);

        _db.Appointments.Single(a => a.Id == id).DepositStatus.Should().Be(DepositStatus.Refunded);
        _db.Payments.Single(p => p.AppointmentId == id).Status.Should().Be(PaymentStatus.Refunded);
    }

    [Fact]
    public async Task Handle_ClientCancelsInsideNoticeWindow_PartialRefundPerDepositRule()
    {
        Guid clientId = SeedClientAsCurrentUser();
        _db.DepositRules.Add(new DepositRule
        {
            StudioId = _studioId, Name = "Lenient", AmountFixed = 50m, IsActive = true,
            CancellationWindowHours = 24, RefundPercentOnLateCancel = 50,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        Guid id = await SeedAppointmentForClient(AppointmentStatus.Confirmed, clientId, DateTime.UtcNow.AddHours(2));
        await SeedPayment(id, PaymentStatus.Paid, ClientPaymentMethod.Card, "pi_client_3");

        await CreateSut().Handle(new CancelAppointmentCommand(id), default);

        await _stripe.Received(1).RefundPaymentIntentAsync("pi_client_3", 2500, Arg.Any<CancellationToken>());
        _db.Appointments.Single(a => a.Id == id).DepositStatus.Should().Be(DepositStatus.Refunded);
        // Regression: revenue reporting distinguishes a partial refund from a full one via
        // RefundedAmount — 50% of a 50 deposit retains 25 for the studio, not 0.
        _db.Payments.Single(p => p.AppointmentId == id).RefundedAmount.Should().Be(25m);
    }

    [Fact]
    public async Task Handle_ClientCancelsOutsideNoticeWindow_SetsRefundedAmountToFullPaymentAmount()
    {
        Guid clientId = SeedClientAsCurrentUser();
        Guid id = await SeedAppointmentForClient(AppointmentStatus.Confirmed, clientId, DateTime.UtcNow.AddDays(5), depositAmount: 80m);
        await SeedPayment(id, PaymentStatus.Paid, ClientPaymentMethod.Card, "pi_client_full", amount: 80m);

        await CreateSut().Handle(new CancelAppointmentCommand(id), default);

        _db.Payments.Single(p => p.AppointmentId == id).RefundedAmount.Should().Be(80m);
    }

    [Fact]
    public async Task Handle_StaffCancelsInsideNoticeWindow_StillRefundsFully()
    {
        // Regression: staff-initiated cancel must be completely unaffected by the client
        // notice-window/refund-percent branch, even for an appointment that's imminent.
        Guid id = await SeedAppointment(AppointmentStatus.Confirmed, DateTime.UtcNow.AddHours(1));
        await SeedPayment(id, PaymentStatus.Paid, ClientPaymentMethod.Card, "pi_staff_1");

        await CreateSut().Handle(new CancelAppointmentCommand(id), default);

        await _stripe.Received(1).RefundPaymentIntentAsync("pi_staff_1", null, Arg.Any<CancellationToken>());
        _db.Appointments.Single(a => a.Id == id).DepositStatus.Should().Be(DepositStatus.Refunded);
        _db.Payments.Single(p => p.AppointmentId == id).RefundedAmount.Should().Be(50m);
    }

    private async Task<Guid> SeedAppointment(AppointmentStatus status) =>
        await SeedAppointment(status, DateTime.UtcNow.AddDays(1));

    private async Task<Guid> SeedAppointment(AppointmentStatus status, DateTime date)
    {
        Appointment appointment = new()
        {
            StudioId        = _studioId,
            ArtistId        = Guid.NewGuid(),
            ClientId        = Guid.NewGuid(),
            Date            = date,
            EndDate         = date.AddHours(2),
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
        Guid appointmentId, PaymentStatus status, ClientPaymentMethod method, string? stripeIntentId,
        decimal amount = 50m)
    {
        _db.Payments.Add(new Payment
        {
            StudioId              = _studioId,
            AppointmentId         = appointmentId,
            ClientId              = Guid.NewGuid(),
            Amount                = amount,
            Status                = status,
            Method                = method,
            StripePaymentIntentId = stripeIntentId,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
