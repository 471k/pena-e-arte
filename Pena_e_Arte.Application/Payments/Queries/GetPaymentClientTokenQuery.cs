using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Queries;

public record GetPaymentClientTokenQuery(Guid PaymentId) : IRequest<PaymentClientTokenResponse>;

public class GetPaymentClientTokenHandler(IAppDbContext db, ICurrentUser currentUser, IJobScheduler jobs)
    : IRequestHandler<GetPaymentClientTokenQuery, PaymentClientTokenResponse>
{
    public async Task<PaymentClientTokenResponse> Handle(
        GetPaymentClientTokenQuery query, CancellationToken ct)
    {
        Payment? payment = await db.Payments
            .FirstOrDefaultAsync(p => p.Id == query.PaymentId, ct);

        if (payment is null)
            throw new NotFoundException(nameof(Payment), query.PaymentId);

        if (currentUser.Role == "client")
        {
            Client? client = await db.Clients
                .FirstOrDefaultAsync(c => c.UserId == currentUser.UserId, ct);

            if (client is null || client.Id != payment.ClientId)
                throw new UnauthorizedAccessException("You can only access your own payment details.");
        }

        // Only after not-found and ownership, so an unauthorised caller learns nothing about the payment's
        // state. The token is what /pay/{id} opens the card form with: never serve one for money that is no
        // longer owed. Same rule and reconciliation trigger as CreateDepositPaymentCommand's cancelled guard.
        AppointmentStatus? appointmentStatus = await db.Appointments
            .Where(a => a.Id == payment.AppointmentId)
            .Select(a => (AppointmentStatus?)a.Status)
            .FirstOrDefaultAsync(ct);

        if (appointmentStatus is null)
            throw new NotFoundException(nameof(Appointment), payment.AppointmentId);

        if (appointmentStatus == AppointmentStatus.Cancelled)
        {
            // A card payment still in flight is settled by PaymentReconciliationJob: released, or refunded
            // in full if it was charged. A client arriving at the dead link is a good moment to run it.
            if (payment is { Method: ClientPaymentMethod.Card, Status: PaymentStatus.Pending, ProviderReferenceId: not null })
                jobs.TriggerPaymentReconciliationNow();
            throw new BusinessRuleViolationException("This appointment was cancelled, so there is nothing to pay.");
        }

        // Paid and Refunded are settled. Failed is only ever set once the POK order behind this token is
        // dead (PaymentReconciliationJob), so it needs a fresh hold, which My Bookings' "Pay deposit" mints
        // on the same row. CashPending is not a card payment. Each message says what is true and where to go.
        string? refusal = payment.Status switch
        {
            PaymentStatus.Pending or PaymentStatus.Captured => null,
            PaymentStatus.Failed =>
                "This card payment can no longer be completed. To pay the deposit, use \"Pay deposit\" on the booking in My Bookings, or pay the studio in cash.",
            PaymentStatus.CashPending =>
                "This deposit is being paid in cash at the studio. To pay by card instead, use \"Pay by card instead\" on the booking in My Bookings.",
            _ => "This payment has already been settled.",
        };
        if (refusal is not null)
            throw new BusinessRuleViolationException(refusal);

        if (payment.ClientToken is null)
            throw new NotFoundException("ClientToken", query.PaymentId);

        // The payment's own studio country: with Currency it tells the checkout page whether to show lek
        // and the "charged in euros" line (Albanian EUR studios), even before any rate exists.
        string? studioCountryCode = await db.Studios
            .Where(s => s.Id == payment.StudioId)
            .Select(s => s.CountryCode)
            .FirstOrDefaultAsync(ct);

        return new PaymentClientTokenResponse(payment.ClientToken, payment.Amount, payment.Currency,
            payment.ReferenceAmount, payment.ReferenceCurrency, studioCountryCode);
    }
}
