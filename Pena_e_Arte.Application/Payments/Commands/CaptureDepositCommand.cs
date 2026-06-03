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
    IAppDbContext         db,
    ICurrentTenant        tenant,
    IStripePaymentService stripePayments,
    IRealtimeNotifier     realtime)
    : IRequestHandler<CaptureDepositCommand, PaymentResponse>
{
    public async Task<PaymentResponse> Handle(CaptureDepositCommand command, CancellationToken ct)
    {
        Payment? payment = await db.Payments
            .Include(p => p.SessionSplits.Where(ss => ss.DeletedAt == null))
            .FirstOrDefaultAsync(p => p.Id == command.PaymentId, ct);
        if (payment is null)
            throw new NotFoundException(nameof(Payment), command.PaymentId);

        if (payment.Status != PaymentStatus.Pending)
            throw new BusinessRuleViolationException("Only authorized (pending) payments can be captured.");

        if (payment.StripePaymentIntentId is null)
            throw new BusinessRuleViolationException("Payment has no associated Stripe intent.");

        Studio studio = await db.Studios.FirstOrDefaultAsync(s => s.Id == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), tenant.StudioId);

        if (studio.StripeAccountId is null)
            throw new StripeAccountNotConnectedException();

        await stripePayments.CapturePaymentAsync(payment.StripePaymentIntentId, studio.StripeAccountId, ct);

        payment.Status    = PaymentStatus.Paid;
        payment.PaidAt    = DateTime.UtcNow;
        payment.UpdatedAt = DateTime.UtcNow;

        Appointment? appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == payment.AppointmentId, ct);
        if (appointment is not null)
        {
            appointment.DepositStatus = DepositStatus.Paid;
            appointment.UpdatedAt     = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        List<SessionSplitResponse> splits = payment.SessionSplits.Select(CreatePaymentIntentHandler.MapSplit).ToList();
        PaymentResponse response = CreatePaymentIntentHandler.Map(payment, splits);
        await realtime.NotifyStudioAsync(tenant.StudioId, "DepositCaptured", response, ct);

        return response;
    }
}
