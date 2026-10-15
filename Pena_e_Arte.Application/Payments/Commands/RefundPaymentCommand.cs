using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Money;

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
    IPaymentProvider paymentProvider,
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
            throw new BusinessRuleViolationException("Payment has no associated provider reference.");

        decimal refundAmount = command.Amount ?? payment.Amount;
        if (refundAmount > payment.Amount)
            throw new BusinessRuleViolationException("Refund amount cannot exceed the original payment amount.");
        if (!CurrencyCatalog.HasAtMostMinorUnits(refundAmount, payment.Currency))
            throw new BusinessRuleViolationException($"Amount has more decimal places than {payment.Currency} allows.");

        await paymentProvider.RefundAsync(
            payment.StudioId, payment.ProviderReferenceId, refundAmount, payment.Currency, ct);

        payment.Status = PaymentStatus.Refunded;
        payment.RefundedAmount = refundAmount;
        payment.UpdatedAt = DateTime.UtcNow;

        // G-14: a full refund means the client no longer has a deposit with the studio — the
        // appointment must say so too (it stayed "Paid", so the client's My bookings still read
        // "Deposit paid"). A partial refund leaves it Paid: the studio still holds part of it.
        Appointment? appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == payment.AppointmentId, ct);
        if (refundAmount >= payment.Amount && appointment is not null)
        {
            appointment.DepositStatus = DepositStatus.Refunded;
            appointment.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        PaymentResponse response = payment.ToResponse() with { AppointmentStatus = appointment?.Status.ToString() };
        await realtime.NotifyStudioAsync(payment.StudioId, "PaymentRefunded", response, ct);
        await sender.Send(new SendPaymentRefundedNotificationCommand(payment.Id), ct);

        return response;
    }
}
