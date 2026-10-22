using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Source of truth for Flow A card payment state (ADR-0001: webhooks and widget callbacks are
/// triggers, never facts). Every pass asks the provider BEFORE changing local state; no pass ever
/// cancels a hold the client has authorised on a live appointment (a hold authorised on a CANCELLED
/// appointment is released). Runs daily (Program.cs, Cron.Daily(2)) and on every POK webhook
/// (JobScheduler.TriggerPaymentReconciliationNow).
/// </summary>
public class PaymentReconciliationJob(
    IAppDbContext db, IPaymentProvider paymentProvider, IJobScheduler jobScheduler, ILogger<PaymentReconciliationJob> logger)
{
    /// <summary>Unpaid card payments on appointments more than this far in the past are abandoned.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(3);

    public async Task RunAsync(CancellationToken ct = default)
    {
        await ReconcileHeldAsync(ct);
        await ReconcilePendingAsync(ct);
    }

    /// <summary>
    /// Held deposits (PaymentStatus.Captured = authorised, not yet captured): promote to Paid when the
    /// provider reports the capture happened elsewhere (e.g. in the POK dashboard); mark Failed when
    /// the provider reports the hold is gone, so "Capture deposit" stops offering an impossible action;
    /// release a hold left on a CANCELLED appointment (unless it is a forfeit, which stays held).
    /// </summary>
    private async Task ReconcileHeldAsync(CancellationToken ct)
    {
        List<Payment> held = await db.Payments
            .IgnoreQueryFilters()
            .Include(p => p.Appointment)
            .Where(p => p.Status == PaymentStatus.Captured
                     && p.Method == ClientPaymentMethod.Card
                     && p.ProviderReferenceId != null
                     && p.DeletedAt == null)
            .ToListAsync(ct);

        foreach (Payment payment in held)
        {
            try
            {
                PaymentProviderStatus? status = await paymentProvider.GetStatusAsync(
                    payment.StudioId, payment.ProviderReferenceId!, ct);
                DateTime now = DateTime.UtcNow;
                bool tellOwnerHoldLost = false;

                switch (status)
                {
                    case PaymentProviderStatus.Captured:
                        MarkPaid(payment, now);
                        if (payment.Appointment.Status == AppointmentStatus.Cancelled)
                            logger.LogWarning(
                                "Held deposit {PaymentId} for studio {StudioId} was captured at the provider on a cancelled appointment; refund it manually.",
                                payment.Id, payment.StudioId);
                        break;
                    case PaymentProviderStatus.Canceled or PaymentProviderStatus.Failed:
                        payment.Status = PaymentStatus.Failed;
                        payment.UpdatedAt = now;
                        logger.LogWarning(
                            "Held deposit {PaymentId} for studio {StudioId} lost its provider hold ({ProviderStatus}); marked Failed.",
                            payment.Id, payment.StudioId, status);
                        // C-15: the owner expected to charge this. Not on a cancelled appointment, where
                        // nothing was left to charge — except a forfeit the studio was keeping.
                        tellOwnerHoldLost = payment.Appointment.Status != AppointmentStatus.Cancelled
                            || payment.Appointment.DepositStatus == DepositStatus.Forfeited;
                        break;
                    case PaymentProviderStatus.Authorized
                        when payment.Appointment.Status == AppointmentStatus.Cancelled
                          && payment.Appointment.DepositStatus != DepositStatus.Forfeited:
                        // The cancellation saw a Pending payment, so it settled nothing; then a heal path
                        // (ConfirmCardPaymentCommand, CreateDepositPaymentCommand's resume branch, which
                        // the saved-card flow shares, or DeclareCashDepositCommand) moved the order to
                        // held without checking the appointment. Release it, exactly like
                        // ReconcilePendingAsync does. A B3 forfeit whose capture POK refused is Forfeited
                        // and stays held for the owner to capture.
                        await paymentProvider.CancelAsync(payment.StudioId, payment.ProviderReferenceId!, ct);
                        payment.Status = PaymentStatus.Refunded;
                        payment.RefundedAmount = payment.Amount;
                        payment.UpdatedAt = now;
                        payment.Appointment.DepositStatus = DepositStatus.Refunded;
                        payment.Appointment.UpdatedAt = now;
                        logger.LogWarning(
                            "Held deposit {PaymentId} for studio {StudioId} was on a cancelled appointment; hold released.",
                            payment.Id, payment.StudioId);
                        break;
                    case PaymentProviderStatus.Authorized:
                        break; // still held
                    default:
                        logger.LogWarning(
                            "Held deposit {PaymentId} for studio {StudioId} has unexpected provider status {ProviderStatus}; left unchanged.",
                            payment.Id, payment.StudioId, status);
                        break;
                }

                // Save per payment, like ReconcilePendingAsync: a provider side effect (the release
                // above) is never left unrecorded because a later payment's save failed.
                await db.SaveChangesAsync(ct);

                // Only after the Failed status is saved, so the notification job finds it.
                if (tellOwnerHoldLost)
                {
                    try
                    {
                        jobScheduler.EnqueueDepositHoldLostNotification(payment.Id);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // The status change is saved; only the email is missed. The payment page still shows Failed.
                        logger.LogError(ex, "Could not queue the hold-lost notification for payment {PaymentId}.", payment.Id);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One studio's provider failure must not abort reconciliation for every other studio.
                logger.LogError(ex, "Failed to reconcile held payment {PaymentId} for studio {StudioId}.", payment.Id, payment.StudioId);
            }
        }
    }

    /// <summary>
    /// Pending card payments: ask the provider FIRST. Heal anything the client already authorised or
    /// paid on a live appointment (the widget's confirm call never ran — closed tab, dropped
    /// connection, 3DS redirect). Release anything authorised on a cancelled appointment. Only then
    /// cancel unpaid orders that are past HoldExpiresAt (legacy rows without one: on an appointment
    /// more than three days in the past) or whose appointment was cancelled. Replaces the former
    /// CancelStalePending/ReleaseExpiredHolds passes, which cancelled authorised holds without checking.
    /// </summary>
    private async Task ReconcilePendingAsync(CancellationToken ct)
    {
        List<Payment> pending = await db.Payments
            .IgnoreQueryFilters()
            .Include(p => p.Appointment)
            .Where(p => p.Status == PaymentStatus.Pending
                     && p.Method == ClientPaymentMethod.Card
                     && p.ProviderReferenceId != null
                     && p.DeletedAt == null)
            .ToListAsync(ct);

        DateTime now = DateTime.UtcNow;
        DateTime staleCutoff = now - StaleAfter;

        foreach (Payment payment in pending)
        {
            string providerReferenceId = payment.ProviderReferenceId!;
            bool appointmentCancelled = payment.Appointment.Status == AppointmentStatus.Cancelled;
            // HoldExpiresAt is authoritative when set (every deposit; every payment link after B4). The
            // appointment-date rule only covers legacy rows without one — a brand-new link for a past
            // appointment must not be cancelled minutes after it was created.
            bool expiredOrStale = payment.HoldExpiresAt is { } expires
                ? expires < now
                : payment.Appointment.Date < staleCutoff;
            bool releasable = expiredOrStale || appointmentCancelled;
            try
            {
                PaymentProviderStatus? status = await paymentProvider.GetStatusAsync(
                    payment.StudioId, providerReferenceId, ct);

                // Provider side effects first. They target THIS order only, so they are right even if
                // B4 has meanwhile released it and minted a fresh order on the same row (B4 releases
                // the old order too).
                if ((status == PaymentProviderStatus.Pending && releasable)
                    || (status == PaymentProviderStatus.Authorized && appointmentCancelled))
                    await paymentProvider.CancelAsync(payment.StudioId, providerReferenceId, ct);

                // Then re-read right before mutating: never apply this order's verdict to a newer order.
                string? currentReference = await db.Payments
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(p => p.Id == payment.Id)
                    .Select(p => p.ProviderReferenceId)
                    .SingleAsync(ct);
                if (currentReference != providerReferenceId)
                    continue;

                switch (status)
                {
                    case PaymentProviderStatus.Authorized when appointmentCancelled:
                        // Authorised after (or racing) the cancellation: the hold was released above;
                        // never heal it to held. Same outcome as cancelling a held deposit with a
                        // full refund (B3). Deliberate: the late-cancel policy is NOT applied here.
                        // The cancellation saw a Pending payment, so it applied no policy, and this
                        // job can't apply one retroactively. The client-favourable release is
                        // recorded in the go-live backlog doc.
                        payment.Status = PaymentStatus.Refunded;
                        payment.RefundedAmount = payment.Amount;
                        payment.UpdatedAt = now;
                        payment.Appointment.DepositStatus = DepositStatus.Refunded;
                        payment.Appointment.UpdatedAt = now;
                        logger.LogWarning(
                            "Pending payment {PaymentId} for studio {StudioId} was authorised on a cancelled appointment; hold released.",
                            payment.Id, payment.StudioId);
                        break;
                    case PaymentProviderStatus.Authorized:
                        payment.Status = PaymentStatus.Captured; // held — the owner captures it
                        payment.UpdatedAt = now;
                        logger.LogInformation(
                            "Pending payment {PaymentId} for studio {StudioId} was authorised at the provider; healed to held.",
                            payment.Id, payment.StudioId);
                        break;
                    case PaymentProviderStatus.Captured:
                        MarkPaid(payment, now);
                        if (appointmentCancelled)
                            logger.LogWarning(
                                "Payment {PaymentId} for studio {StudioId} was captured at the provider on a cancelled appointment; refund it manually.",
                                payment.Id, payment.StudioId);
                        break;
                    case PaymentProviderStatus.Pending when releasable:
                        payment.Status = PaymentStatus.Failed; // the unpaid order was cancelled above
                        payment.UpdatedAt = now;
                        break;
                    case PaymentProviderStatus.Canceled or PaymentProviderStatus.Failed:
                    case null when releasable:
                        payment.Status = PaymentStatus.Failed; // nothing left to cancel at the provider
                        payment.UpdatedAt = now;
                        break;
                    case PaymentProviderStatus.Refunded:
                        logger.LogWarning(
                            "Pending payment {PaymentId} for studio {StudioId} is refunded at the provider; left unchanged for manual review.",
                            payment.Id, payment.StudioId);
                        break;
                        // Pending or null, not yet releasable: leave for a later run.
                }

                // Save per payment: only DB work (no provider call) between the re-read and this write.
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to reconcile pending payment {PaymentId} for studio {StudioId}.", payment.Id, payment.StudioId);
            }
        }
    }

    private static void MarkPaid(Payment payment, DateTime now)
    {
        payment.Status = PaymentStatus.Paid;
        payment.PaidAt = now;
        payment.UpdatedAt = now;
        // A forfeited deposit that is captured later (after B3's failed forfeit capture) stays
        // Forfeited: that is the more informative state. This covers a capture made in the POK
        // dashboard; B3 adds the same guard to CaptureDepositCommand for the in-app button.
        if (payment.Appointment.DepositStatus != DepositStatus.Forfeited)
            payment.Appointment.DepositStatus = DepositStatus.Paid;
        payment.Appointment.UpdatedAt = now;
    }
}
