using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Commands;

public record CaptureDepositCommand(Guid PaymentId) : IRequest<PaymentResponse>;

public class CaptureDepositHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    IPaymentProvider stripePayments,
    IRealtimeNotifier realtime,
    ISender sender)
    : IRequestHandler<CaptureDepositCommand, PaymentResponse>
{
    public async Task<PaymentResponse> Handle(CaptureDepositCommand command, CancellationToken ct)
    {
        Payment? payment = await db.Payments
            .Include(p => p.Client)
            .FirstOrDefaultAsync(p => p.Id == command.PaymentId, ct);
        if (payment is null)
            throw new NotFoundException(nameof(Payment), command.PaymentId);

        if (payment.Status == PaymentStatus.Pending)
            throw new BusinessRuleViolationException(
                "The client has not completed card authorization yet — the deposit cannot be captured.");

        if (payment.Status != PaymentStatus.Captured)
            throw new BusinessRuleViolationException("Only authorized (held) deposits can be captured.");

        if (payment.ProviderReferenceId is null)
            throw new BusinessRuleViolationException("Payment has no associated Stripe intent.");

        await stripePayments.CaptureAsync(payment.ProviderReferenceId, ct);

        payment.Status = PaymentStatus.Paid;
        payment.PaidAt = DateTime.UtcNow;
        payment.UpdatedAt = DateTime.UtcNow;

        Appointment? appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == payment.AppointmentId, ct);
        if (appointment is not null)
        {
            appointment.DepositStatus = DepositStatus.Paid;
            appointment.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        PaymentResponse response = payment.ToResponse();
        await realtime.NotifyStudioAsync(tenant.StudioId, "DepositCaptured", response, ct);

        await sender.Send(new SendDepositCapturedNotificationCommand(payment.Id), ct);

        return response;
    }
}
