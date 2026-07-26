using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Payments.Commands;

public record MarkPaymentFailedCommand(string StripePaymentIntentId) : IRequest;

public class MarkPaymentFailedHandler(IAppDbContext db)
    : IRequestHandler<MarkPaymentFailedCommand>
{
    public async Task Handle(MarkPaymentFailedCommand command, CancellationToken ct)
    {
        // Webhook-only path — see ConfirmPaymentCommand for rationale on IgnoreQueryFilters.
        Domain.Entities.Payment? payment = await db.Payments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.StripePaymentIntentId == command.StripePaymentIntentId, ct);

        if (payment is null || payment.Status == PaymentStatus.Failed) return;

        payment.Status = PaymentStatus.Failed;
        payment.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}
