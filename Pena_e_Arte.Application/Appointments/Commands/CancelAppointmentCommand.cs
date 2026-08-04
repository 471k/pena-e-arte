using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;

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
    IPaymentProvider paymentProvider)
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

        // Cancel scheduled reminder jobs before they fire
        jobs.CancelAppointmentJobs(appointment.ReminderJobId48h, appointment.ReminderJobId24h);

        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancellationReason = isClient ? CancellationReason.ClientCancelled : command.Reason;
        appointment.UpdatedAt = DateTime.UtcNow;

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

                if (refundPercent >= 100)
                {
                    await paymentProvider.RefundAsync(payment.ProviderReferenceId, null, ct);
                    payment.Status = PaymentStatus.Refunded;
                    payment.RefundedAmount = payment.Amount;
                    appointment.DepositStatus = DepositStatus.Refunded;
                }
                else if (refundPercent > 0)
                {
                    decimal refundAmount = Math.Round(
                        appointment.DepositAmount * refundPercent / 100m, 2, MidpointRounding.AwayFromZero);
                    long refundCents = (long)Math.Round(refundAmount * 100m, MidpointRounding.AwayFromZero);
                    await paymentProvider.RefundAsync(payment.ProviderReferenceId, refundCents, ct);
                    payment.Status = PaymentStatus.Refunded;
                    payment.RefundedAmount = refundAmount;
                    appointment.DepositStatus = DepositStatus.Refunded;
                }
                else
                {
                    // Late cancellation, 0% refund configured — deposit is forfeited outright,
                    // no Stripe call: the studio keeps the already-authorized/captured amount.
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
        await realtime.NotifyStudioAsync(
            tenant.StudioId, "AppointmentCancelled", new { command.AppointmentId }, ct);

        await sender.Send(new SendAppointmentCancellationCommand(appointment.Id), ct);
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
