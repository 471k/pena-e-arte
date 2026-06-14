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

/// <summary>
/// Client-facing card deposit: creates (or resumes) the Stripe PaymentIntent for the
/// caller's own appointment. The amount always comes from the appointment's
/// DepositAmount — never from the request — so a client cannot choose what to pay.
/// </summary>
public record CreateDepositPaymentCommand(Guid AppointmentId) : IRequest<PaymentIntentResponse>;

public class CreateDepositPaymentHandler(
    IAppDbContext         db,
    ICurrentTenant        tenant,
    ICurrentUser          currentUser,
    IStripePaymentService stripePayments)
    : IRequestHandler<CreateDepositPaymentCommand, PaymentIntentResponse>
{
    public async Task<PaymentIntentResponse> Handle(CreateDepositPaymentCommand command, CancellationToken ct)
    {
        Appointment appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Appointment), command.AppointmentId);

        // Clients may only pay the deposit on their own appointment —
        // ownership resolved (and healed) through Client.UserId / email.
        if (currentUser.Role == "client")
        {
            Client? me = await db.FindClientForUserAsync(currentUser, ct);
            if (me is null || me.Id != appointment.ClientId)
                throw new NotFoundException(nameof(Appointment), command.AppointmentId);
        }

        if (appointment.DepositAmount <= 0)
            throw new BusinessRuleViolationException("This appointment does not require a deposit.");

        // Single payment row per appointment (unique index) — failed attempts and
        // cash declarations are converted in place, never duplicated.
        Payment? existing = await db.Payments
            .FirstOrDefaultAsync(p => p.AppointmentId == appointment.Id, ct);

        // An unauthorized card intent may be resumable — but never trust the stored
        // secret blindly: reconcile with Stripe first. This also heals local state
        // when webhooks were missed (e.g. the client authorized but we never heard).
        if (existing is { Method: ClientPaymentMethod.Card, Status: PaymentStatus.Pending,
                          ClientSecret: not null, StripePaymentIntentId: not null })
        {
            string? piStatus = await stripePayments.GetPaymentIntentStatusAsync(
                existing.StripePaymentIntentId, ct);

            switch (piStatus)
            {
                case "requires_payment_method" or "requires_confirmation" or "requires_action" or "processing":
                    // Still awaiting the client — resume with the same intent
                    return new PaymentIntentResponse(existing.Id, existing.ClientSecret, PaymentStatus.Pending.ToString());

                case "requires_capture":
                    // Authorized but the webhook never arrived — heal and report
                    existing.Status    = PaymentStatus.Captured;
                    existing.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    return new PaymentIntentResponse(existing.Id, existing.ClientSecret, PaymentStatus.Captured.ToString());

                case "succeeded":
                    // Captured but the webhook never arrived — heal and report
                    existing.Status    = PaymentStatus.Paid;
                    existing.PaidAt    = DateTime.UtcNow;
                    existing.UpdatedAt = DateTime.UtcNow;
                    appointment.DepositStatus = DepositStatus.Paid;
                    appointment.UpdatedAt     = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    return new PaymentIntentResponse(existing.Id, existing.ClientSecret, PaymentStatus.Paid.ToString());

                // canceled / gone at Stripe — fall through and mint a fresh intent
            }
        }

        bool convertible = existing is { Method: ClientPaymentMethod.Cash, Status: PaymentStatus.CashPending }
                        or { Method: ClientPaymentMethod.Card, Status: PaymentStatus.Pending }
                        or { Status: PaymentStatus.Failed };
        if (existing is not null && !convertible)
            throw new BusinessRuleViolationException("A payment for this appointment is already in progress.");

        Guid paymentId     = existing?.Id ?? Guid.NewGuid();
        long amountInCents = (long)(appointment.DepositAmount * 100);

        (string intentId, string clientSecret) = await stripePayments.CreatePaymentIntentAsync(
            amountInCents, "EUR", paymentId, ct);

        if (existing is null)
        {
            db.Payments.Add(new Payment
            {
                Id                    = paymentId,
                StudioId              = tenant.StudioId,
                AppointmentId         = appointment.Id,
                ClientId              = appointment.ClientId,
                Amount                = appointment.DepositAmount,
                Status                = PaymentStatus.Pending,
                Method                = ClientPaymentMethod.Card,
                StripePaymentIntentId = intentId,
                ClientSecret          = clientSecret,
            });
        }
        else
        {
            // Convert in place: cash declaration switched to card, or a failed attempt retried
            existing.Method                = ClientPaymentMethod.Card;
            existing.Status                = PaymentStatus.Pending;
            existing.StripePaymentIntentId = intentId;
            existing.ClientSecret          = clientSecret;
            existing.CashNote              = null;
            existing.UpdatedAt             = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return new PaymentIntentResponse(paymentId, clientSecret, PaymentStatus.Pending.ToString());
    }
}
