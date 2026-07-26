using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record CompleteAppointmentCommand(Guid AppointmentId) : IRequest<AppointmentResponse>;

public class CompleteAppointmentHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    IRealtimeNotifier realtime,
    ISender sender,
    IStripePaymentService stripe)
    : IRequestHandler<CompleteAppointmentCommand, AppointmentResponse>
{
    public async Task<AppointmentResponse> Handle(CompleteAppointmentCommand command, CancellationToken ct)
    {
        Domain.Entities.Appointment appointment = await db.Appointments
            .Include(a => a.Attachments)
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Appointment), command.AppointmentId);

        if (appointment.Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow)
            throw new BusinessRuleViolationException(
                $"Cannot complete an appointment with status {appointment.Status}.");

        appointment.Status = AppointmentStatus.Completed;
        appointment.UpdatedAt = DateTime.UtcNow;

        // Capture the Stripe hold that was authorised at booking time
        Domain.Entities.Payment? payment = await db.Payments
            .FirstOrDefaultAsync(p =>
                p.AppointmentId == appointment.Id &&
                p.Status == PaymentStatus.Captured &&
                p.Method == ClientPaymentMethod.Card, ct);

        if (payment is not null && !string.IsNullOrEmpty(payment.StripePaymentIntentId))
        {
            await stripe.CapturePaymentAsync(payment.StripePaymentIntentId, ct);
            payment.Status = PaymentStatus.Paid;
            payment.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        AppointmentResponse response = CreateAppointmentHandler.Map(appointment);
        await realtime.NotifyStudioAsync(
            tenant.StudioId, "AppointmentCompleted", response, ct);

        await sender.Send(new SendAftercareNotificationCommand(appointment.Id), ct);

        return response;
    }
}
