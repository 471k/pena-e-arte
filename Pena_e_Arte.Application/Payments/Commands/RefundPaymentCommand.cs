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

        // C-14: a payment can be refunded several times until the whole amount is back (POK allows
        // several partial refunds — proven on staging 2026-10-16). There is no "PartiallyRefunded"
        // status: a partly refunded payment is Refunded with RefundedAmount < Amount.
        // A Refunded payment without a RefundedAmount predates that column — it was a full refund.
        decimal alreadyRefunded = payment.RefundedAmount
            ?? (payment.Status == PaymentStatus.Refunded ? payment.Amount : 0m);
        bool partlyRefunded = payment.Status == PaymentStatus.Refunded && alreadyRefunded < payment.Amount;
        if (payment.Status != PaymentStatus.Paid && !partlyRefunded)
            throw new BusinessRuleViolationException("Only paid payments with an amount left to refund can be refunded.");

        if (payment.ProviderReferenceId is null)
            throw new BusinessRuleViolationException("Payment has no associated provider reference.");

        decimal remaining = payment.Amount - alreadyRefunded;
        decimal refundAmount = command.Amount ?? remaining;
        if (refundAmount > remaining)
            throw new BusinessRuleViolationException(alreadyRefunded > 0m
                ? $"Refund amount cannot exceed the {MoneyText.Format(remaining, payment.Currency)} left to refund on this payment."
                : "Refund amount cannot exceed the original payment amount.");
        if (!CurrencyCatalog.HasAtMostMinorUnits(refundAmount, payment.Currency))
            throw new BusinessRuleViolationException($"Amount has more decimal places than {payment.Currency} allows.");

        await paymentProvider.RefundAsync(
            payment.StudioId, payment.ProviderReferenceId, refundAmount, payment.Currency, ct);

        payment.Status = PaymentStatus.Refunded;
        payment.RefundedAmount = alreadyRefunded + refundAmount;
        payment.UpdatedAt = DateTime.UtcNow;

        // G-14: a full refund means the client no longer has a deposit with the studio — the
        // appointment must say so too (it stayed "Paid", so the client's My bookings still read
        // "Deposit paid"). A partial refund leaves it Paid: the studio still holds part of it.
        // With several refunds, "full" is the running total reaching the amount.
        Appointment? appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == payment.AppointmentId, ct);
        if (payment.RefundedAmount >= payment.Amount && appointment is not null)
        {
            appointment.DepositStatus = DepositStatus.Refunded;
            appointment.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        PaymentResponse response = payment.ToResponse() with { AppointmentStatus = appointment?.Status.ToString() };
        await realtime.NotifyStudioAsync(payment.StudioId, "PaymentRefunded", response, ct);
        // The client is told what THIS refund gave back — not the payment's whole amount.
        await sender.Send(new SendPaymentRefundedNotificationCommand(payment.Id, refundAmount), ct);

        return response;
    }
}
