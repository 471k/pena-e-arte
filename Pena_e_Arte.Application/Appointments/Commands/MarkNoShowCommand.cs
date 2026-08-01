using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Commands;

public record MarkNoShowCommand(Guid AppointmentId) : IRequest<AppointmentResponse>;

public class MarkNoShowHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    IRealtimeNotifier realtime,
    IPaymentProvider paymentProvider)
    : IRequestHandler<MarkNoShowCommand, AppointmentResponse>
{
    public async Task<AppointmentResponse> Handle(MarkNoShowCommand command, CancellationToken ct)
    {
        Domain.Entities.Appointment appointment = await db.Appointments
            .Include(a => a.Attachments)
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Appointment), command.AppointmentId);

        if (appointment.Status is AppointmentStatus.Cancelled or AppointmentStatus.Completed)
            throw new BusinessRuleViolationException(
                $"Cannot mark no-show for an appointment with status {appointment.Status}.");

        appointment.Status = AppointmentStatus.NoShow;
        appointment.DepositStatus = DepositStatus.Forfeited;
        appointment.UpdatedAt = DateTime.UtcNow;

        Domain.Entities.Payment? payment = await db.Payments
            .FirstOrDefaultAsync(p =>
                p.AppointmentId == appointment.Id &&
                p.Status != PaymentStatus.Refunded, ct);

        if (payment is not null)
        {
            if (payment.Method == ClientPaymentMethod.Card
                && !string.IsNullOrEmpty(payment.ProviderReferenceId)
                && payment.Status == PaymentStatus.Captured)
            {
                await paymentProvider.CaptureAsync(payment.ProviderReferenceId, ct);
                payment.Status = PaymentStatus.Paid;
                payment.UpdatedAt = DateTime.UtcNow;
            }
            else if (payment.Status == PaymentStatus.CashPending)
            {
                // Cash deposit declared but not yet confirmed — forfeit the record
                payment.Status = PaymentStatus.Paid;
                payment.UpdatedAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);

        AppointmentResponse response = CreateAppointmentHandler.Map(appointment);
        await realtime.NotifyStudioAsync(
            tenant.StudioId, "AppointmentNoShow", response, ct);

        return response;
    }
}
