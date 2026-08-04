using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Payments.Commands;

public record ConfirmPaymentCommand(string ProviderReferenceId) : IRequest;

public class ConfirmPaymentHandler(IAppDbContext db)
    : IRequestHandler<ConfirmPaymentCommand>
{
    public async Task Handle(ConfirmPaymentCommand command, CancellationToken ct)
    {
        // Webhook-only path: Stripe server-to-server call has no tenant JWT.
        // IgnoreQueryFilters is intentional here — this is a cross-tenant system operation
        // secured at the endpoint level by Stripe-Signature HMAC validation.
        Domain.Entities.Payment? payment = await db.Payments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.ProviderReferenceId == command.ProviderReferenceId, ct);

        if (payment is null || payment.Status == PaymentStatus.Paid) return;

        payment.Status = PaymentStatus.Paid;
        payment.PaidAt = DateTime.UtcNow;
        payment.UpdatedAt = DateTime.UtcNow;

        // Webhook-only path — IgnoreQueryFilters intentional (no tenant JWT in scope).
        Appointment? appointment = await db.Appointments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == payment.AppointmentId, ct);

        if (appointment is not null)
        {
            appointment.DepositStatus = DepositStatus.Paid;
            appointment.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
