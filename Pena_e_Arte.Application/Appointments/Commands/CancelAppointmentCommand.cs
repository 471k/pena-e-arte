using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Money;
using Pena_e_Arte.Domain.Services;
using Pena_e_Arte.Application.Waitlists.Commands;
using Pena_e_Arte.Application.Waitlists.Common;

namespace Pena_e_Arte.Application.Appointments.Commands;

// AuditStudioId is left at its default (null) — AuditLogBehavior falls back to the caller's
// ICurrentTenant.StudioId, which is always set for this tenant-scoped command (both the
// client self-cancel and staff-cancel paths). The audit entry's ActorRole records which
// role actually performed the cancellation.
public record CancelAppointmentCommand(
    Guid AppointmentId,
    CancellationReason Reason = CancellationReason.StudioCancelled) : IRequest, IAuditableCommand
{
    public string AuditAction => AuditActions.AppointmentCancelled;
    public string AuditTargetType => AuditTargetTypes.Appointment;
    public Guid AuditTargetId => AppointmentId;
}

public class CancelAppointmentHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    IRealtimeNotifier realtime,
    ISender sender,
    IJobScheduler jobs,
    IPaymentProvider paymentProvider,
    ILogger<CancelAppointmentHandler> logger)
    : IRequestHandler<CancelAppointmentCommand>
{
    public async Task Handle(CancelAppointmentCommand command, CancellationToken ct)
    {
        Domain.Entities.Appointment appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Appointment), command.AppointmentId);

        bool isClient = currentUser.Role == "client";

        // A client may only cancel their own appointment. 404 (not 403) on mismatch — matches
        // ReviewDesignHandler's scope-violation convention so a guessed appointment id doesn't
        // confirm a valid-but-not-theirs resource exists.
        if (isClient)
        {
            Client? me = await db.FindClientForUserAsync(currentUser, ct);
            if (me is null || me.Id != appointment.ClientId)
                throw new NotFoundException(nameof(Domain.Entities.Appointment), command.AppointmentId);
        }

        if (appointment.Status == AppointmentStatus.Cancelled)
            return;

        if (isClient)
        {
            // Self-cancel is only offered for Pending/Confirmed bookings — matches the
            // "manage your booking" affordance shown on MyBookingsSection.
            if (appointment.Status is not (AppointmentStatus.Pending or AppointmentStatus.Confirmed))
                throw new BusinessRuleViolationException(
                    $"A {appointment.Status} appointment can no longer be self-cancelled.");
        }
        else if (appointment.Status == AppointmentStatus.Completed)
        {
            throw new BusinessRuleViolationException("Completed appointments cannot be cancelled.");
        }

        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancellationReason = isClient ? CancellationReason.ClientCancelled : command.Reason;
        appointment.UpdatedAt = DateTime.UtcNow;

        // Auto-FIFO waitlist match: notify only the first still-Waiting entry that covers this
        // now-freed slot (ArtistId null-or-matching, PreferredDateFrom/To covering the
        // appointment's date). The entity mutation is included in this same SaveChangesAsync
        // below; the actual notification dispatch happens after the existing
        // SendAppointmentCancellationCommand send, matching this handler's own "notify after
        // save" convention.
        Guid? waitlistMatchId = await db.ClaimNextMatchAsync(
            appointment.StudioId, appointment.ArtistId, appointment.Date, ct);

        // Refund deposit. Studio-initiated cancellation always refunds 100% (unchanged).
        // Client self-cancellation refunds per ClientCancellationPolicy — full refund with
        // enough notice, otherwise the studio's configured late-cancel percentage (0% by
        // default, i.e. the deposit is forfeited). DepositStatus is only ever set to Refunded
        // when a refund actually happened — a Pending/Failed card intent never took the
        // client's money, so there's nothing to refund.
        Domain.Entities.Payment? payment = await db.Payments
            .FirstOrDefaultAsync(p => p.AppointmentId == appointment.Id, ct);

        if (payment is not null)
        {
            if (payment.Method == ClientPaymentMethod.Card
                && !string.IsNullOrEmpty(payment.ProviderReferenceId)
                && (payment.Status == PaymentStatus.Captured || payment.Status == PaymentStatus.Paid))
            {
                int refundPercent = isClient
                    ? await ResolveClientRefundPercentAsync(appointment, ct)
                    : 100;

                if (payment.Status == PaymentStatus.Captured)
                {
                    await SettleHeldDepositAsync(payment, appointment, refundPercent, ct);
                }
                else if (refundPercent >= 100)
                {
                    // Explicit amount even for a full refund: POK rejects a refund without one.
                    await paymentProvider.RefundAsync(payment.StudioId, payment.ProviderReferenceId, payment.Amount, payment.Currency, ct);
                    payment.Status = PaymentStatus.Refunded;
                    payment.RefundedAmount = payment.Amount;
                    appointment.DepositStatus = DepositStatus.Refunded;
                }
                else if (refundPercent > 0)
                {
                    decimal refundAmount = CurrencyCatalog.Round(
                        appointment.DepositAmount * refundPercent / 100m, payment.Currency);
                    await paymentProvider.RefundAsync(payment.StudioId, payment.ProviderReferenceId, refundAmount, payment.Currency, ct);
                    payment.Status = PaymentStatus.Refunded;
                    payment.RefundedAmount = refundAmount;
                    appointment.DepositStatus = DepositStatus.Refunded;
                }
                else
                {
                    // Late cancellation, 0% refund — the deposit was already captured (Paid), so the
                    // studio keeps it with no provider call.
                    appointment.DepositStatus = DepositStatus.Forfeited;
                }

                payment.UpdatedAt = DateTime.UtcNow;
            }
            else if (payment.Status == PaymentStatus.CashPending)
            {
                // Deliberately NOT subject to ClientCancellationPolicy, for both staff and
                // client cancellations: CashPending means the client only declared an intent
                // to pay cash (DeclareCashDepositCommand) — no money has actually changed
                // hands yet (that only happens via ConfirmCashDepositCommand, which moves the
                // payment to Paid). There is nothing to forfeit or partially refund from an
                // amount that was never collected, regardless of how much notice was given —
                // mirrors the no-op behavior for an unauthorized/never-captured card payment.
                payment.Status = PaymentStatus.Refunded;
                payment.UpdatedAt = DateTime.UtcNow;
                appointment.DepositStatus = DepositStatus.Refunded;
            }
        }

        await db.SaveChangesAsync(ct);

        // Cancel scheduled reminder jobs only once the cancellation is saved: a provider failure
        // above (e.g. POK refusing a partial capture) refuses the cancellation, and the appointment
        // must keep its reminders.
        jobs.CancelAppointmentJobs(appointment.ReminderJobId48h, appointment.ReminderJobId24h);

        await realtime.NotifyStudioAsync(
            tenant.StudioId, "AppointmentCancelled", new { command.AppointmentId }, ct);

        await sender.Send(new SendAppointmentCancellationCommand(appointment.Id), ct);
        jobs.EnqueueWebhookDelivery(tenant.StudioId, "appointment.cancelled", appointment.Id);

        if (waitlistMatchId is Guid matchId)
            await sender.Send(new SendWaitlistSlotAvailableNotificationCommand(matchId), ct);
    }

    /// <summary>
    /// A held deposit (PaymentStatus.Captured = authorised, never captured) has nothing to refund:
    /// releasing the hold IS the refund, and keeping part of it means capturing only that part
    /// (POK: one capture per order, capped at the authorised amount; the release gate's T5b
    /// verifies that the remainder is released). Amounts are based on payment.Amount, the
    /// authorised amount. The refund share is rounded first, exactly like the Paid branch, so both
    /// paths round the same way.
    /// </summary>
    private async Task SettleHeldDepositAsync(
        Domain.Entities.Payment payment, Domain.Entities.Appointment appointment, int refundPercent, CancellationToken ct)
    {
        string providerReferenceId = payment.ProviderReferenceId!;
        decimal refund = refundPercent >= 100
            ? payment.Amount
            : CurrencyCatalog.Round(payment.Amount * Math.Max(refundPercent, 0) / 100m, payment.Currency);
        decimal keep = payment.Amount - refund;

        if (keep <= 0m)
        {
            // Full refund: release the hold.
            try
            {
                await paymentProvider.CancelAsync(payment.StudioId, providerReferenceId, ct);
            }
            catch (ServiceUnavailableException)
            {
                // POK answered, but refused the release. If it reports the hold already gone (lapsed,
                // cancelled, never paid or not found), there is nothing to release, so the
                // cancellation goes ahead. If the hold is still there or was already charged (e.g.
                // captured in the POK dashboard), rethrow: the cancellation is refused and retried,
                // never marked released on a guess. A failed status read also propagates.
                PaymentProviderStatus? status = await paymentProvider.GetStatusAsync(
                    payment.StudioId, providerReferenceId, ct);
                if (status is PaymentProviderStatus.Authorized or PaymentProviderStatus.Captured
                    or PaymentProviderStatus.Refunded)
                    throw;
                logger.LogWarning(
                    "POK refused to release the hold for payment {PaymentId} in studio {StudioId}, but reports it gone ({ProviderStatus}); cancelling anyway.",
                    payment.Id, payment.StudioId, status);
            }
            // Any other failure (studio not connected, POK unreachable or timed out) propagates:
            // 422/500, the cancellation is refused and the caller retries. The refund path for an
            // already-charged deposit behaves the same way.
            payment.Status = PaymentStatus.Refunded;
            payment.RefundedAmount = payment.Amount;
            appointment.DepositStatus = DepositStatus.Refunded;
            return;
        }

        if (refund <= 0m)
        {
            // Forfeit (0%): the studio keeps the whole deposit, which for a held deposit means
            // capturing it — the old code made no provider call, so the hold lapsed and nothing was
            // collected. If the capture fails for ANY provider reason (POK error, order gone, POK
            // unreachable or timed out, studio not connected), never block the cancellation: the
            // deposit stays on hold ("Capture deposit" still works), and PaymentReconciliationJob
            // promotes it if POK did capture, or marks it Failed if the hold is gone. Only the
            // caller's own cancellation propagates. The try holds nothing but the provider call and
            // two property sets, so the broad catch hides nothing else.
            try
            {
                await paymentProvider.CaptureAsync(payment.StudioId, providerReferenceId, ct);
                payment.Status = PaymentStatus.Paid;
                payment.PaidAt = DateTime.UtcNow;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                // Never pass ex: NotFoundException's message carries the full POK order id.
                logger.LogWarning(
                    "Forfeit capture failed ({ErrorType}) for payment {PaymentId} in studio {StudioId}; the deposit stays on hold for the owner to capture.",
                    ex.GetType().Name, payment.Id, payment.StudioId);
            }
            appointment.DepositStatus = DepositStatus.Forfeited;
            return;
        }

        // Partial: capture the share the studio keeps; POK releases the rest. A failure propagates
        // (503, or 422/500 when the studio isn't connected or POK is unreachable), so the
        // cancellation is refused and the client retries — the outcome Phi confirmed at the
        // release gate's C0 (D2). Never fall back to a full capture: that would charge the client
        // more than the policy allows. If the hold is already gone, the client stays blocked until
        // PaymentReconciliationJob marks the payment Failed.
        await paymentProvider.CaptureAsync(payment.StudioId, providerReferenceId, keep, payment.Currency, ct);
        payment.Status = PaymentStatus.Refunded;           // partial "refund" = the released remainder
        payment.PaidAt = DateTime.UtcNow;                  // revenue counts the kept share (PaidAt != null)
        payment.RefundedAmount = refund;
        appointment.DepositStatus = DepositStatus.Refunded;
    }

    private async Task<int> ResolveClientRefundPercentAsync(
        Domain.Entities.Appointment appointment, CancellationToken ct)
    {
        DepositRule? rule = await db.DepositRules
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        return ClientCancellationPolicy.ResolveRefundPercent(rule, appointment.Date, DateTime.UtcNow);
    }
}
