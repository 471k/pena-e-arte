using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Commands;

// AuditStudioId left at its default (null) — AuditLogBehavior falls back to the caller's
// ICurrentTenant.StudioId, always set for this OwnerOnly tenant-scoped command.
public record RefundPaymentCommand(Guid PaymentId, decimal? Amount)
    : IRequest<PaymentResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.PaymentRefunded;
    public string AuditTargetType => AuditTargetTypes.Payment;
    public Guid AuditTargetId => PaymentId;
}

public class RefundPaymentHandler(
    IAppDbContext db,
    IPaymentProvider stripePayments,
    IRealtimeNotifier realtime,
    ISender sender)
    : IRequestHandler<RefundPaymentCommand, PaymentResponse>
{
    public async Task<PaymentResponse> Handle(RefundPaymentCommand command, CancellationToken ct)
    {
        Payment? payment = await db.Payments
            .Include(p => p.Client)
            .FirstOrDefaultAsync(p => p.Id == command.PaymentId, ct);
        if (payment is null)
            throw new NotFoundException(nameof(Payment), command.PaymentId);

        if (payment.Status != PaymentStatus.Paid)
            throw new BusinessRuleViolationException("Only paid payments can be refunded.");

        if (payment.ProviderReferenceId is null)
            throw new BusinessRuleViolationException("Payment has no associated Stripe intent.");

        decimal refundAmount = command.Amount ?? payment.Amount;
        if (refundAmount > payment.Amount)
            throw new BusinessRuleViolationException("Refund amount cannot exceed the original payment amount.");

        long amountInCents = (long)(refundAmount * 100);
        await stripePayments.RefundAsync(
            payment.ProviderReferenceId, amountInCents, ct);

        payment.Status = PaymentStatus.Refunded;
        payment.RefundedAmount = refundAmount;
        payment.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        await realtime.NotifyStudioAsync(payment.StudioId, "PaymentRefunded", payment.ToResponse(), ct);
        await sender.Send(new SendPaymentRefundedNotificationCommand(payment.Id), ct);

        return payment.ToResponse();
    }
}
