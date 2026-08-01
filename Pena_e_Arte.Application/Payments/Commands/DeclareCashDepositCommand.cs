using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Commands;

public record DeclareCashDepositCommand(Guid AppointmentId, string? Note) : IRequest<PaymentResponse>;

public class DeclareCashDepositHandler(
    IAppDbContext db,
    ICurrentTenant tenant,
    ICurrentUser currentUser,
    IPaymentProvider stripePayments)
    : IRequestHandler<DeclareCashDepositCommand, PaymentResponse>
{
    public async Task<PaymentResponse> Handle(DeclareCashDepositCommand command, CancellationToken ct)
    {
        Appointment? appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Appointment), command.AppointmentId);

        // Clients may only declare cash for their own appointment —
        // ownership resolved (and healed) through Client.UserId / email.
        if (currentUser.Role == "client")
        {
            Client? me = await db.FindClientForUserAsync(currentUser, ct);
            if (me is null || me.Id != appointment.ClientId)
                throw new NotFoundException(nameof(Appointment), command.AppointmentId);
        }

        // Single payment row per appointment (unique index) — unauthorized card intents
        // and failed attempts are converted in place, never duplicated.
        Payment? existing = await db.Payments
            .FirstOrDefaultAsync(p => p.AppointmentId == command.AppointmentId, ct);

        if (existing is { Method: ClientPaymentMethod.Card, Status: PaymentStatus.Pending }
                     or { Status: PaymentStatus.Failed })
        {
            // Client changed their mind before authorizing, or retries after a failure.
            // Reconcile with Stripe first — if the card was already authorized/captured
            // (webhook missed), heal the local state instead of discarding the hold.
            if (existing.ProviderReferenceId is not null && existing.Status == PaymentStatus.Pending)
            {
                string? piStatus = await stripePayments.GetStatusAsync(
                    existing.ProviderReferenceId, ct);

                if (piStatus == "requires_capture")
                {
                    existing.Status = PaymentStatus.Captured;
                    existing.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    throw new BusinessRuleViolationException(
                        "The card deposit is already authorized — there is nothing to pay in cash.");
                }

                if (piStatus == "succeeded")
                {
                    existing.Status = PaymentStatus.Paid;
                    existing.PaidAt = DateTime.UtcNow;
                    existing.UpdatedAt = DateTime.UtcNow;
                    appointment.DepositStatus = DepositStatus.Paid;
                    appointment.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    throw new BusinessRuleViolationException("The deposit has already been paid by card.");
                }

                // Cancel only intents that can still be cancelled; canceled/missing need no call
                if (piStatus is "requires_payment_method" or "requires_confirmation" or "requires_action" or "processing")
                    await stripePayments.CancelAsync(existing.ProviderReferenceId, ct);
            }

            existing.Method = ClientPaymentMethod.Cash;
            existing.Status = PaymentStatus.CashPending;
            existing.CashNote = command.Note;
            existing.ProviderReferenceId = null;
            existing.ClientSecret = null;
            existing.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
            return existing.ToResponse();
        }

        if (existing is not null)
            throw new BusinessRuleViolationException("A payment record already exists for this appointment.");

        Payment payment = new()
        {
            StudioId = tenant.StudioId,
            AppointmentId = appointment.Id,
            ClientId = appointment.ClientId,
            Amount = appointment.DepositAmount,
            Method = ClientPaymentMethod.Cash,
            Status = PaymentStatus.CashPending,
            CashNote = command.Note,
        };

        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        return payment.ToResponse();
    }
}
