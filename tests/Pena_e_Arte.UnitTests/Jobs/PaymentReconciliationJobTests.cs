using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Jobs;

public class PaymentReconciliationJobTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly FakeDbContext _db;
    private readonly IPaymentProvider _stripe = Substitute.For<IPaymentProvider>();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly Guid _studioId = Guid.NewGuid();

    public PaymentReconciliationJobTests()
    {
        _db = FakeDbContext.Create(_dbName);
    }

    private PaymentReconciliationJob CreateSut() => new(_db, _stripe, _jobs, NullLogger<PaymentReconciliationJob>.Instance);

    // ── Held deposits (PaymentStatus.Captured) ───────────────────────────────────

    [Fact]
    public async Task RunAsync_CapturedPaymentProviderCaptured_MarksAsPaid()
    {
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_test_001");
        _stripe.GetStatusAsync(_studioId, "pi_test_001", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Captured);

        await CreateSut().RunAsync();

        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public async Task RunAsync_CapturedPaymentProviderCaptured_SetsPaidAt()
    {
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_test_002");
        _stripe.GetStatusAsync(_studioId, "pi_test_002", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Captured);

        await CreateSut().RunAsync();

        _db.Payments.Find(payment.Id)!.PaidAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RunAsync_CapturedPaymentProviderAuthorized_DoesNotMarkAsPaid()
    {
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_test_003");
        _stripe.GetStatusAsync(_studioId, "pi_test_003", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Authorized);

        await CreateSut().RunAsync();

        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Captured);
    }

    [Fact]
    public async Task RunAsync_CapturedPaymentProviderNull_DoesNotMarkAsPaid()
    {
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_test_004");
        _stripe.GetStatusAsync(_studioId, "pi_test_004", Arg.Any<CancellationToken>())
               .Returns((PaymentProviderStatus?)null);

        await CreateSut().RunAsync();

        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Captured);
    }

    [Fact]
    public async Task RunAsync_HeldPaymentCapturedAtProvider_SetsDepositStatusPaid()
    {
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(5));
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_held_paid", appointmentId);
        _stripe.GetStatusAsync(_studioId, "pi_held_paid", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Captured);

        await CreateSut().RunAsync();

        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Paid);
        _db.Appointments.Find(appointmentId)!.DepositStatus.Should().Be(DepositStatus.Paid);
        // Captured in the POK dashboard: the client gets the same email as for "Capture deposit".
        _jobs.Received(1).EnqueueDepositPaidNotification(payment.Id);
    }

    // ── Charged when the client pays (C-4, 2026-10-22) ─────────────────────────────

    [Fact]
    public async Task RunAsync_PendingDepositPaidAtProvider_MarksPaidAndQueuesTheClientEmail()
    {
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(5));
        Payment payment = await SeedPayment(PaymentStatus.Pending, "pi_paid_now", appointmentId);
        _stripe.GetStatusAsync(_studioId, "pi_paid_now", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Captured);

        await CreateSut().RunAsync();
        await CreateSut().RunAsync(); // now Paid: not reconciled again, so no second email

        Payment updated = _db.Payments.Find(payment.Id)!;
        updated.Status.Should().Be(PaymentStatus.Paid);
        updated.PaidAt.Should().NotBeNull();
        _db.Appointments.Find(appointmentId)!.DepositStatus.Should().Be(DepositStatus.Paid);
        _jobs.Received(1).EnqueueDepositPaidNotification(payment.Id);
    }

    [Fact]
    public async Task RunAsync_DepositPaidOnACancelledAppointment_IsRefundedInFull()
    {
        Guid appointmentId = await SeedAppointment(
            DateTime.UtcNow.AddDays(5), DepositStatus.Pending, AppointmentStatus.Cancelled);
        Payment payment = await SeedPayment(PaymentStatus.Pending, "pi_paid_after_cancel", appointmentId);
        _stripe.GetStatusAsync(_studioId, "pi_paid_after_cancel", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Captured);

        await CreateSut().RunAsync();

        await _stripe.Received(1).RefundAsync(_studioId, "pi_paid_after_cancel", 50m, Arg.Any<string>(), Arg.Any<CancellationToken>());
        Payment updated = _db.Payments.Find(payment.Id)!;
        updated.Status.Should().Be(PaymentStatus.Refunded);
        updated.RefundedAmount.Should().Be(updated.Amount);
        updated.PaidAt.Should().NotBeNull(); // charged, then refunded: "Deposit refunded", retained 0
        _db.Appointments.Find(appointmentId)!.DepositStatus.Should().Be(DepositStatus.Refunded);
        _jobs.DidNotReceive().EnqueueDepositPaidNotification(Arg.Any<Guid>());
    }

    [Fact]
    public async Task RunAsync_RefundOfAPaymentOnACancelledAppointmentFails_LeavesItPendingForTheNextRun()
    {
        Guid appointmentId = await SeedAppointment(
            DateTime.UtcNow.AddDays(5), DepositStatus.Pending, AppointmentStatus.Cancelled);
        Payment payment = await SeedPayment(PaymentStatus.Pending, "pi_refund_down", appointmentId);
        _stripe.GetStatusAsync(_studioId, "pi_refund_down", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Captured);
        _stripe.RefundAsync(_studioId, "pi_refund_down", 50m, Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns<Task<string>>(_ => throw new ServiceUnavailableException("POK unavailable"));

        await CreateSut().RunAsync();

        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Pending);
        _jobs.DidNotReceive().EnqueueDepositPaidNotification(Arg.Any<Guid>());
    }

    [Fact]
    public async Task RunAsync_HeldPaymentCanceledAtProvider_MarksFailed()
    {
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_held_canceled");
        _stripe.GetStatusAsync(_studioId, "pi_held_canceled", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Canceled);

        await CreateSut().RunAsync();

        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Failed);
        await _stripe.DidNotReceive().CancelAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_HeldPaymentFailedAtProvider_MarksFailed()
    {
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_held_failed");
        _stripe.GetStatusAsync(_studioId, "pi_held_failed", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Failed);

        await CreateSut().RunAsync();

        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Failed);
    }

    // ── C-15: tell the owner when a held deposit is lost ─────────────────────────

    [Theory]
    [InlineData(PaymentProviderStatus.Canceled)]
    [InlineData(PaymentProviderStatus.Failed)]
    public async Task RunAsync_HeldPaymentLostOnLiveAppointment_QueuesOwnerNotificationOnce(PaymentProviderStatus lost)
    {
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_hold_lost_live");
        _stripe.GetStatusAsync(_studioId, "pi_hold_lost_live", Arg.Any<CancellationToken>()).Returns(lost);

        await CreateSut().RunAsync();
        await CreateSut().RunAsync(); // now Failed: no longer in the held pass, so no second notification

        _jobs.Received(1).EnqueueDepositHoldLostNotification(payment.Id);
    }

    [Fact]
    public async Task RunAsync_HeldPaymentLostOnCompletedAppointment_QueuesOwnerNotification()
    {
        // The owner forgot to charge it after the session: exactly the money C-15 is about.
        Guid appointmentId = await SeedAppointment(
            DateTime.UtcNow.AddDays(-2), DepositStatus.Pending, AppointmentStatus.Completed);
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_hold_lost_done", appointmentId);
        _stripe.GetStatusAsync(_studioId, "pi_hold_lost_done", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Canceled);

        await CreateSut().RunAsync();

        _jobs.Received(1).EnqueueDepositHoldLostNotification(payment.Id);
    }

    [Fact]
    public async Task RunAsync_HeldForfeitLostOnCancelledAppointment_QueuesOwnerNotification()
    {
        Guid appointmentId = await SeedAppointment(
            DateTime.UtcNow.AddDays(-1), DepositStatus.Forfeited, AppointmentStatus.Cancelled);
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_forfeit_lost", appointmentId);
        _stripe.GetStatusAsync(_studioId, "pi_forfeit_lost", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Canceled);

        await CreateSut().RunAsync();

        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Failed);
        _jobs.Received(1).EnqueueDepositHoldLostNotification(payment.Id);
    }

    [Fact]
    public async Task RunAsync_HeldPaymentLostOnCancelledAppointment_DoesNotNotifyOwner()
    {
        Guid appointmentId = await SeedAppointment(
            DateTime.UtcNow.AddDays(5), DepositStatus.Refunded, AppointmentStatus.Cancelled);
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_lost_cancelled", appointmentId);
        _stripe.GetStatusAsync(_studioId, "pi_lost_cancelled", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Canceled);

        await CreateSut().RunAsync();

        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Failed);
        _jobs.DidNotReceive().EnqueueDepositHoldLostNotification(Arg.Any<Guid>());
    }

    [Fact]
    public async Task RunAsync_HeldPaymentStillAuthorized_DoesNotNotifyOwner()
    {
        await SeedPayment(PaymentStatus.Captured, "pi_still_held");
        _stripe.GetStatusAsync(_studioId, "pi_still_held", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Authorized);

        await CreateSut().RunAsync();

        _jobs.DidNotReceive().EnqueueDepositHoldLostNotification(Arg.Any<Guid>());
    }

    [Fact]
    public async Task RunAsync_PendingOrderCancelled_DoesNotNotifyOwner()
    {
        // An unpaid order that never became a hold is not a lost deposit.
        await SeedPayment(PaymentStatus.Pending, "pi_pending_gone", holdExpiresAt: DateTime.UtcNow.AddHours(-1));
        _stripe.GetStatusAsync(_studioId, "pi_pending_gone", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Canceled);

        await CreateSut().RunAsync();

        _jobs.DidNotReceive().EnqueueDepositHoldLostNotification(Arg.Any<Guid>());
    }

    [Fact]
    public async Task RunAsync_NotificationCannotBeQueued_StillMarksFailed()
    {
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_queue_down");
        _stripe.GetStatusAsync(_studioId, "pi_queue_down", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Canceled);
        _jobs.When(j => j.EnqueueDepositHoldLostNotification(Arg.Any<Guid>()))
             .Do(_ => throw new InvalidOperationException("job storage down"));

        await CreateSut().RunAsync();

        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_HeldPaymentCapturedOnForfeitedAppointment_KeepsForfeited()
    {
        Guid appointmentId = await SeedAppointment(
            DateTime.UtcNow.AddDays(-1), DepositStatus.Forfeited, AppointmentStatus.Cancelled);
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_held_forfeit_paid", appointmentId);
        _stripe.GetStatusAsync(_studioId, "pi_held_forfeit_paid", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Captured);

        await CreateSut().RunAsync();

        Payment updated = _db.Payments.Find(payment.Id)!;
        updated.Status.Should().Be(PaymentStatus.Paid);
        updated.PaidAt.Should().NotBeNull();
        _db.Appointments.Find(appointmentId)!.DepositStatus.Should().Be(DepositStatus.Forfeited);
    }

    [Fact]
    public async Task RunAsync_HeldPaymentAuthorizedOnCancelledAppointment_ReleasesHold()
    {
        // Cancel-then-heal race: the cancellation saw a Pending payment and settled nothing, then a
        // heal path moved the order to held without checking the appointment.
        Guid appointmentId = await SeedAppointment(
            DateTime.UtcNow.AddDays(5), DepositStatus.Pending, AppointmentStatus.Cancelled);
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_held_on_cancelled", appointmentId);
        _stripe.GetStatusAsync(_studioId, "pi_held_on_cancelled", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Authorized);

        await CreateSut().RunAsync();

        await _stripe.Received(1).CancelAsync(_studioId, "pi_held_on_cancelled", Arg.Any<CancellationToken>());
        Payment updated = _db.Payments.Find(payment.Id)!;
        updated.Status.Should().Be(PaymentStatus.Refunded);
        updated.RefundedAmount.Should().Be(updated.Amount);
        _db.Appointments.Find(appointmentId)!.DepositStatus.Should().Be(DepositStatus.Refunded);
    }

    [Fact]
    public async Task RunAsync_HeldForfeitedDepositOnCancelledAppointment_StaysHeld()
    {
        Guid appointmentId = await SeedAppointment(
            DateTime.UtcNow.AddDays(5), DepositStatus.Forfeited, AppointmentStatus.Cancelled);
        Payment payment = await SeedPayment(PaymentStatus.Captured, "pi_held_forfeit", appointmentId);
        _stripe.GetStatusAsync(_studioId, "pi_held_forfeit", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Authorized);

        await CreateSut().RunAsync();

        await _stripe.DidNotReceive().CancelAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Captured);
    }

    // ── Pending card payments: stale-appointment rule (legacy rows without HoldExpiresAt) ──

    [Fact]
    public async Task RunAsync_PendingPaymentOnPastAppointment_CancelsAndMarksFailed()
    {
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(-5));
        Payment payment = await SeedPayment(PaymentStatus.Pending, "pi_stale_001", appointmentId);
        _stripe.GetStatusAsync(_studioId, "pi_stale_001", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Pending);

        await CreateSut().RunAsync();

        await _stripe.Received(1).CancelAsync(_studioId, "pi_stale_001", Arg.Any<CancellationToken>());
        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_PendingPaymentOnFutureAppointment_DoesNotCancel()
    {
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(5));
        Payment payment = await SeedPayment(PaymentStatus.Pending, "pi_future_001", appointmentId);
        _stripe.GetStatusAsync(_studioId, "pi_future_001", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Pending);

        await CreateSut().RunAsync();

        await _stripe.DidNotReceive().CancelAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task RunAsync_PendingPaymentWithin3DayCutoff_DoesNotCancel()
    {
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(-2));
        Payment payment = await SeedPayment(PaymentStatus.Pending, "pi_recent_001", appointmentId);
        _stripe.GetStatusAsync(_studioId, "pi_recent_001", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Pending);

        await CreateSut().RunAsync();

        await _stripe.DidNotReceive().CancelAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Pending);
    }

    // ── Pending card payments: HoldExpiresAt (PENA-106) ──────────────────────────

    [Fact]
    public async Task RunAsync_PendingCardHoldPastExpiry_CancelsAndMarksFailed()
    {
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(5));
        Payment payment = await SeedPayment(
            PaymentStatus.Pending, "pi_hold_expired", appointmentId, DateTime.UtcNow.AddMinutes(-5));
        _stripe.GetStatusAsync(_studioId, "pi_hold_expired", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Pending);

        await CreateSut().RunAsync();

        await _stripe.Received(1).CancelAsync(_studioId, "pi_hold_expired", Arg.Any<CancellationToken>());
        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_PendingCardHoldNotYetExpired_DoesNotCancel()
    {
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(5));
        Payment payment = await SeedPayment(
            PaymentStatus.Pending, "pi_hold_active", appointmentId, DateTime.UtcNow.AddMinutes(30));
        _stripe.GetStatusAsync(_studioId, "pi_hold_active", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Pending);

        await CreateSut().RunAsync();

        await _stripe.DidNotReceive().CancelAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task RunAsync_PendingLinkWithFutureExpiryOnPastAppointment_NotCancelled()
    {
        // HoldExpiresAt is authoritative when set: a fresh link for a past appointment must not be
        // cancelled by the legacy stale-appointment rule.
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(-5));
        Payment payment = await SeedPayment(
            PaymentStatus.Pending, "pi_link_fresh", appointmentId, DateTime.UtcNow.AddHours(12));
        _stripe.GetStatusAsync(_studioId, "pi_link_fresh", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Pending);

        await CreateSut().RunAsync();

        await _stripe.DidNotReceive().CancelAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Pending);
    }

    // ── Pending card payments: ask the provider first (B1, Finding 2) ────────────

    [Fact]
    public async Task RunAsync_PendingPaymentAuthorizedAtProvider_HealsToHeldAndNeverCancels()
    {
        // Core regression for Finding 2: the client authorised but the app's confirm call never
        // ran. Even past HoldExpiresAt, the authorised hold must be healed, never cancelled.
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(5));
        Payment payment = await SeedPayment(
            PaymentStatus.Pending, "pi_authorised", appointmentId, DateTime.UtcNow.AddMinutes(-5));
        _stripe.GetStatusAsync(_studioId, "pi_authorised", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Authorized);

        await CreateSut().RunAsync();

        await _stripe.DidNotReceive().CancelAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Captured);
    }

    [Fact]
    public async Task RunAsync_PendingPaymentAuthorizedOnCancelledAppointment_ReleasesHold()
    {
        Guid appointmentId = await SeedAppointment(
            DateTime.UtcNow.AddDays(5), DepositStatus.Pending, AppointmentStatus.Cancelled);
        Payment payment = await SeedPayment(
            PaymentStatus.Pending, "pi_auth_on_cancelled", appointmentId, DateTime.UtcNow.AddHours(12));
        _stripe.GetStatusAsync(_studioId, "pi_auth_on_cancelled", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Authorized);

        await CreateSut().RunAsync();

        await _stripe.Received(1).CancelAsync(_studioId, "pi_auth_on_cancelled", Arg.Any<CancellationToken>());
        Payment updated = _db.Payments.Find(payment.Id)!;
        updated.Status.Should().Be(PaymentStatus.Refunded);
        updated.RefundedAmount.Should().Be(updated.Amount);
        _db.Appointments.Find(appointmentId)!.DepositStatus.Should().Be(DepositStatus.Refunded);
    }

    [Fact]
    public async Task RunAsync_PendingUnpaidOnCancelledAppointment_CancelsOrderBeforeExpiry()
    {
        Guid appointmentId = await SeedAppointment(
            DateTime.UtcNow.AddDays(5), DepositStatus.Pending, AppointmentStatus.Cancelled);
        Payment payment = await SeedPayment(
            PaymentStatus.Pending, "pi_unpaid_on_cancelled", appointmentId, DateTime.UtcNow.AddHours(12));
        _stripe.GetStatusAsync(_studioId, "pi_unpaid_on_cancelled", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Pending);

        await CreateSut().RunAsync();

        await _stripe.Received(1).CancelAsync(_studioId, "pi_unpaid_on_cancelled", Arg.Any<CancellationToken>());
        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_PendingPaymentCapturedAtProvider_MarksPaidAndDepositPaid()
    {
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(5));
        Payment payment = await SeedPayment(
            PaymentStatus.Pending, "pi_pending_paid", appointmentId, DateTime.UtcNow.AddHours(12));
        _stripe.GetStatusAsync(_studioId, "pi_pending_paid", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Captured);

        await CreateSut().RunAsync();

        Payment updated = _db.Payments.Find(payment.Id)!;
        updated.Status.Should().Be(PaymentStatus.Paid);
        updated.PaidAt.Should().NotBeNull();
        _db.Appointments.Find(appointmentId)!.DepositStatus.Should().Be(DepositStatus.Paid);
        await _stripe.DidNotReceive().CancelAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_PendingPaymentCanceledAtProvider_MarksFailedWithoutCancelCall()
    {
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(5));
        Payment payment = await SeedPayment(
            PaymentStatus.Pending, "pi_pending_canceled", appointmentId, DateTime.UtcNow.AddHours(12));
        _stripe.GetStatusAsync(_studioId, "pi_pending_canceled", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Canceled);

        await CreateSut().RunAsync();

        await _stripe.DidNotReceive().CancelAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_PendingPaymentNotFoundAndExpired_MarksFailedWithoutCancelCall()
    {
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(5));
        Payment payment = await SeedPayment(
            PaymentStatus.Pending, "pi_gone_expired", appointmentId, DateTime.UtcNow.AddMinutes(-5));
        _stripe.GetStatusAsync(_studioId, "pi_gone_expired", Arg.Any<CancellationToken>())
               .Returns((PaymentProviderStatus?)null);

        await CreateSut().RunAsync();

        await _stripe.DidNotReceive().CancelAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Failed);
    }

    [Fact]
    public async Task RunAsync_PendingPaymentNotFoundNotExpired_LeftPending()
    {
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(5));
        Payment payment = await SeedPayment(
            PaymentStatus.Pending, "pi_gone_active", appointmentId, DateTime.UtcNow.AddHours(12));
        _stripe.GetStatusAsync(_studioId, "pi_gone_active", Arg.Any<CancellationToken>())
               .Returns((PaymentProviderStatus?)null);

        await CreateSut().RunAsync();

        await _stripe.DidNotReceive().CancelAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task RunAsync_PendingPaymentRefundedAtProvider_LeftUnchanged()
    {
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(5));
        Payment payment = await SeedPayment(
            PaymentStatus.Pending, "pi_pending_refunded", appointmentId, DateTime.UtcNow.AddMinutes(-5));
        _stripe.GetStatusAsync(_studioId, "pi_pending_refunded", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Refunded);

        await CreateSut().RunAsync();

        await _stripe.DidNotReceive().CancelAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public async Task RunAsync_PendingPaymentReMintedDuringRun_IsLeftAlone()
    {
        // Releasable (expired) + provider Pending: without the re-read guard the job would mark
        // the row Failed even though a fresh order was minted on it meanwhile.
        Guid appointmentId = await SeedAppointment(DateTime.UtcNow.AddDays(5));
        Payment payment = await SeedPayment(
            PaymentStatus.Pending, "pi_old_order", appointmentId, DateTime.UtcNow.AddMinutes(-5));
        _stripe.GetStatusAsync(_studioId, "pi_old_order", Arg.Any<CancellationToken>())
               .Returns(_ =>
               {
                   using FakeDbContext other = FakeDbContext.Create(_dbName);
                   other.Payments.Find(payment.Id)!.ProviderReferenceId = "pi_new_order";
                   other.SaveChanges();
                   return PaymentProviderStatus.Pending;
               });

        await CreateSut().RunAsync();

        await _stripe.Received(1).CancelAsync(_studioId, "pi_old_order", Arg.Any<CancellationToken>());
        await _stripe.DidNotReceive().CancelAsync(Arg.Any<Guid>(), "pi_new_order", Arg.Any<CancellationToken>());
        using FakeDbContext fresh = FakeDbContext.Create(_dbName);
        Payment stored = fresh.Payments.Find(payment.Id)!;
        stored.Status.Should().Be(PaymentStatus.Pending);
        stored.ProviderReferenceId.Should().Be("pi_new_order");
    }

    [Fact]
    public async Task RunAsync_OneStudioProviderThrows_OtherStudiosStillReconciled()
    {
        Guid otherStudioId = Guid.NewGuid();
        Payment broken = await SeedPayment(PaymentStatus.Captured, "pi_broken");
        Payment healthy = await SeedPayment(PaymentStatus.Captured, "pi_healthy", studioId: otherStudioId);
        _stripe.GetStatusAsync(_studioId, "pi_broken", Arg.Any<CancellationToken>())
               .ThrowsAsync(new PaymentProviderNotConnectedException("Reconnect POK."));
        _stripe.GetStatusAsync(otherStudioId, "pi_healthy", Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Captured);

        await CreateSut().RunAsync();

        _db.Payments.Find(broken.Id)!.Status.Should().Be(PaymentStatus.Captured);
        _db.Payments.Find(healthy.Id)!.Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public async Task RunAsync_AlreadyPaidPayment_IsNotTouched()
    {
        Payment payment = await SeedPayment(PaymentStatus.Paid, "pi_paid_001");
        _stripe.GetStatusAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns(PaymentProviderStatus.Captured);

        await CreateSut().RunAsync();

        _db.Payments.Find(payment.Id)!.Status.Should().Be(PaymentStatus.Paid);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private async Task<Payment> SeedPayment(
        PaymentStatus status,
        string intentId,
        Guid? appointmentId = null,
        DateTime? holdExpiresAt = null,
        Guid? studioId = null)
    {
        // The job Includes the (required) Appointment navigation, so every payment needs a real
        // appointment row or it silently drops out of the pass.
        Payment payment = new()
        {
            StudioId = studioId ?? _studioId,
            AppointmentId = appointmentId ?? await SeedAppointment(DateTime.UtcNow.AddDays(5), studioId: studioId),
            ClientId = Guid.NewGuid(),
            Amount = 50m,
            Status = status,
            Method = ClientPaymentMethod.Card,
            ProviderReferenceId = intentId,
            HoldExpiresAt = holdExpiresAt,
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment;
    }

    private async Task<Guid> SeedAppointment(
        DateTime date,
        DepositStatus depositStatus = DepositStatus.Pending,
        AppointmentStatus status = AppointmentStatus.Confirmed,
        Guid? studioId = null)
    {
        Appointment appt = new()
        {
            StudioId = studioId ?? _studioId,
            ArtistId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Date = date,
            EndDate = date.AddHours(2),
            DurationMinutes = 120,
            Status = status,
            DepositStatus = depositStatus,
        };
        _db.Appointments.Add(appt);
        await _db.SaveChangesAsync();
        return appt.Id;
    }
}
