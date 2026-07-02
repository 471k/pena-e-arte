using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record CancelAppointmentCommand(
    Guid               AppointmentId,
    CancellationReason Reason = CancellationReason.StudioCancelled) : IRequest;

public class CancelAppointmentHandler(
    IAppDbContext        db,
    ICurrentTenant       tenant,
    IRealtimeNotifier    realtime,
    ISender              sender,
    IJobScheduler        jobs,
    IStripePaymentService stripe)
    : IRequestHandler<CancelAppointmentCommand>
{
    public async Task Handle(CancelAppointmentCommand command, CancellationToken ct)
    {
        Domain.Entities.Appointment appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Appointment), command.AppointmentId);

        if (appointment.Status == AppointmentStatus.Cancelled)
            return;

        if (appointment.Status == AppointmentStatus.Completed)
            throw new BusinessRuleViolationException("Completed appointments cannot be cancelled.");

        // Cancel scheduled reminder jobs before they fire
        jobs.CancelAppointmentJobs(appointment.ReminderJobId48h, appointment.ReminderJobId24h);

        appointment.Status             = AppointmentStatus.Cancelled;
        appointment.CancellationReason = command.Reason;
        appointment.UpdatedAt          = DateTime.UtcNow;

        // Refund deposit on studio-initiated cancellation. DepositStatus is only ever
        // set to Refunded when a refund actually happened — a Pending/Failed card
        // intent never took the client's money, so there's nothing to refund.
        Domain.Entities.Payment? payment = await db.Payments
            .FirstOrDefaultAsync(p => p.AppointmentId == appointment.Id, ct);

        if (payment is not null)
        {
            if (payment.Method == ClientPaymentMethod.Card
                && !string.IsNullOrEmpty(payment.StripePaymentIntentId)
                && (payment.Status == PaymentStatus.Captured || payment.Status == PaymentStatus.Paid))
            {
                await stripe.RefundPaymentIntentAsync(payment.StripePaymentIntentId, null, ct);
                payment.Status    = PaymentStatus.Refunded;
                payment.UpdatedAt = DateTime.UtcNow;
                appointment.DepositStatus = DepositStatus.Refunded;
            }
            else if (payment.Status == PaymentStatus.CashPending)
            {
                payment.Status    = PaymentStatus.Refunded;
                payment.UpdatedAt = DateTime.UtcNow;
                appointment.DepositStatus = DepositStatus.Refunded;
            }
        }

        await db.SaveChangesAsync(ct);
        await realtime.NotifyStudioAsync(
            tenant.StudioId, "AppointmentCancelled", new { command.AppointmentId }, ct);

        await sender.Send(new SendAppointmentCancellationCommand(appointment.Id), ct);
    }
}
