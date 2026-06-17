using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Jobs;

public class PaymentReconciliationJob(IAppDbContext db, IStripePaymentService stripe)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        await ReconcileCapturedAsync(ct);
        await CancelStalePendingAsync(ct);
    }

    private async Task ReconcileCapturedAsync(CancellationToken ct)
    {
        List<Payment> captured = await db.Payments
            .IgnoreQueryFilters()
            .Where(p => p.Status == PaymentStatus.Captured
                     && p.Method == ClientPaymentMethod.Card
                     && p.StripePaymentIntentId != null
                     && p.DeletedAt == null)
            .ToListAsync(ct);

        foreach (Payment payment in captured)
        {
            string? status = await stripe.GetPaymentIntentStatusAsync(payment.StripePaymentIntentId!, ct);
            if (status is "succeeded")
            {
                payment.Status = PaymentStatus.Paid;
                payment.PaidAt = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task CancelStalePendingAsync(CancellationToken ct)
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-3);

        List<Payment> stale = await db.Payments
            .IgnoreQueryFilters()
            .Include(p => p.Appointment)
            .Where(p => p.Status == PaymentStatus.Pending
                     && p.Method == ClientPaymentMethod.Card
                     && p.StripePaymentIntentId != null
                     && p.DeletedAt == null
                     && p.Appointment.Date < cutoff)
            .ToListAsync(ct);

        foreach (Payment payment in stale)
        {
            await stripe.CancelPaymentIntentAsync(payment.StripePaymentIntentId!, ct);
            payment.Status = PaymentStatus.Failed;
        }

        await db.SaveChangesAsync(ct);
    }
}
