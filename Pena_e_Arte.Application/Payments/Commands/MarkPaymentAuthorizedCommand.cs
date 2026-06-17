using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Commands;

/// <summary>
/// Fired from the Stripe webhook when a manual-capture PaymentIntent becomes capturable
/// (payment_intent.amount_capturable_updated) — the client has authorized the card.
/// Moves the payment from Pending to Captured (authorized, held, not yet captured).
/// </summary>
public record MarkPaymentAuthorizedCommand(string StripePaymentIntentId) : IRequest;

public class MarkPaymentAuthorizedHandler(IAppDbContext db, IRealtimeNotifier realtime)
    : IRequestHandler<MarkPaymentAuthorizedCommand>
{
    public async Task Handle(MarkPaymentAuthorizedCommand command, CancellationToken ct)
    {
        // Webhook-only path — see ConfirmPaymentCommand for rationale on IgnoreQueryFilters.
        Domain.Entities.Payment? payment = await db.Payments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.StripePaymentIntentId == command.StripePaymentIntentId, ct);

        // Only the Pending → Captured transition is valid; everything else is a stale event
        if (payment is null || payment.Status != PaymentStatus.Pending) return;

        payment.Status    = PaymentStatus.Captured;
        payment.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        await realtime.NotifyStudioAsync(
            payment.StudioId, "PaymentAuthorized",
            new { paymentId = payment.Id, appointmentId = payment.AppointmentId, status = "Captured" },
            ct);
    }
}
