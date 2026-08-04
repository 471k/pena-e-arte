using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Jobs;

public class PaymentReconciliationJobTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IPaymentProvider _stripe = Substitute.For<IPaymentProvider>();
    private readonly Guid _studioId = Guid.NewGuid();

    private PaymentReconciliationJob CreateSut() => new(_db, _stripe);

    // ── ReconcileCaptured ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_CapturedPaymentStripeSucceeded_MarksAsPaid()
    {
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_test_001");
        _stripe.GetStatusAsync("pi_test_001", Arg.Any<CancellationToken>())
               .Returns("succeeded");

        await CreateSut().RunAsync();

        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public async Task RunAsync_CapturedPaymentStripeSucceeded_SetsPaidAt()
    {
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_test_002");
        _stripe.GetStatusAsync("pi_test_002", Arg.Any<CancellationToken>())
               .Returns("succeeded");

        await CreateSut().RunAsync();

        _db.Payments.Find(payment.Id)!.PaidAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_CapturedPaymentStripePending_DoesNotMarkAsPaid()
    {
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_test_003");
        _stripe.GetStatusAsync("pi_test_003", Arg.Any<CancellationToken>())
               .Returns("requires_capture");

        await CreateSut().RunAsync();

        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Captured);
    }

    [Fact]
    public async Task RunAsync_CapturedPaymentStripeNull_DoesNotMarkAsPaid()
    {
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_test_004");
        _stripe.GetStatusAsync("pi_test_004", Arg.Any<CancellationToken>())
               .Returns((string?)null);

        await CreateSut().RunAsync();

        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Captured);
    }

    // ── CancelStalePending ────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_PendingPaymentOnPastAppointment_CancelsAndMarksFailed()
    {
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(-5));
        Payment payment = await SeedPayment(PaymentStatus.Pending, "pi_stale_001", appointmentId);

        await CreateSut().RunAsync();

        await _stripe.Received(1).CancelAsync("pi_stale_001", Arg.Any<CancellationToken>());
        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_PendingPaymentOnFutureAppointment_DoesNotCancel()
    {
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(5));
        Payment payment = await SeedPayment(PaymentStatus.Pending, "pi_future_001", appointmentId);

        await CreateSut().RunAsync();

        await _stripe.DidNotReceive().CancelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task RunAsync_PendingPaymentWithin3DayCutoff_DoesNotCancel()
    {
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(-2));
        Payment payment = await SeedPayment(PaymentStatus.Pending, "pi_recent_001", appointmentId);

        await CreateSut().RunAsync();

        await _stripe.DidNotReceive().CancelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Pending);
    }

    // ── ReleaseExpiredHolds (PENA-106) ────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_PendingCardHoldPastExpiry_CancelsAndMarksFailed()
    {
        // Future appointment so CancelStalePending does NOT match — isolates the hold-expiry pass.
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(5));
        Payment payment = new()
        {
            StudioId = _studioId,
            AppointmentId = appointmentId,
            ClientId = Guid.NewGuid(),
            Amount = 50m,
            Status = PaymentStatus.Pending,
            Method = ClientPaymentMethod.Card,
            ProviderReferenceId = "pi_hold_expired",
            HoldExpiresAt = DateTime.UtcNow.AddMinutes(-5),
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();

        await CreateSut().RunAsync();

        await _stripe.Received(1).CancelAsync("pi_hold_expired", Arg.Any<CancellationToken>());
        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_PendingCardHoldNotYetExpired_DoesNotCancel()
    {
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(5));
        Payment payment = new()
        {
            StudioId = _studioId,
            AppointmentId = appointmentId,
            ClientId = Guid.NewGuid(),
            Amount = 50m,
            Status = PaymentStatus.Pending,
            Method = ClientPaymentMethod.Card,
            ProviderReferenceId = "pi_hold_active",
            HoldExpiresAt = DateTime.UtcNow.AddMinutes(30),
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();

        await CreateSut().RunAsync();

        await _stripe.DidNotReceive().CancelAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task RunAsync_AlreadyPaidPayment_IsNotTouched()
    {
        Payment payment = await SeedPayment(PaymentStatus.Paid, "pi_paid_001");
        _stripe.GetStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns("succeeded");

        await CreateSut().RunAsync();

        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Paid);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private async Task<Payment> SeedPayment(
        PaymentStatus status,
        string intentId,
        Guid? appointmentId = null)
    {
        Payment payment = new()
        {
            StudioId = _studioId,
            AppointmentId = appointmentId ?? Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Amount = 50m,
            Status = status,
            Method = ClientPaymentMethod.Card,
            ProviderReferenceId = intentId,
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment;
    }

    private async Task<Guid> SeedAppointment(DateTime date)
    {
        Appointment appt = new()
        {
            StudioId = _studioId,
            ArtistId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Date = date,
            EndDate = date.AddHours(2),
            DurationMinutes = 120,
            Status = AppointmentStatus.Completed,
            DepositStatus = DepositStatus.Paid,
        };
        _db.Appointments.Add(appt);
        await _db.SaveChangesAsync();
        return appt.Id;
    }
}
