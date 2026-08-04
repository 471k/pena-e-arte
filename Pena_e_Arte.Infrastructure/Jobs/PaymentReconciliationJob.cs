using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Jobs;

public class PaymentReconciliationJob(IAppDbContext db, IPaymentProvider paymentProvider)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        await ReconcileCapturedAsync(ct);
        await CancelStalePendingAsync(ct);
        await ReleaseExpiredHoldsAsync(ct);
    }

    private async Task ReconcileCapturedAsync(CancellationToken ct)
    {
        List<Payment> captured = await db.Payments
            .IgnoreQueryFilters()
            .Where(p => p.Status == PaymentStatus.Captured
                     && p.Method == ClientPaymentMethod.Card
                     && p.ProviderReferenceId != null
                     && p.DeletedAt == null)
            .ToListAsync(ct);

        foreach (Payment payment in captured)
        {
            string? status = await paymentProvider.GetStatusAsync(payment.ProviderReferenceId!, ct);
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
                     && p.ProviderReferenceId != null
                     && p.DeletedAt == null
                     && p.Appointment.Date < cutoff)
            .ToListAsync(ct);

        foreach (Payment payment in stale)
        {
            await paymentProvider.CancelAsync(payment.ProviderReferenceId!, ct);
            payment.Status = PaymentStatus.Failed;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Third pass (PENA-106): auto-release authorization holds past their server-enforced
    /// <see cref="Payment.HoldExpiresAt"/> (maps onto POK's expiresAfterMinutes). This job already
    /// owns time-based payment-state transitions, so the expiry check lives here rather than in a
    /// separate fourth job.
    /// </summary>
    private async Task ReleaseExpiredHoldsAsync(CancellationToken ct)
    {
        DateTime now = DateTime.UtcNow;

        List<Payment> expiredHolds = await db.Payments
            .IgnoreQueryFilters()
            .Where(p => p.Status == PaymentStatus.Pending
                     && p.Method == ClientPaymentMethod.Card
                     && p.ProviderReferenceId != null
                     && p.DeletedAt == null
                     && p.HoldExpiresAt != null
                     && p.HoldExpiresAt < now)
            .ToListAsync(ct);

        foreach (Payment payment in expiredHolds)
        {
            await paymentProvider.CancelAsync(payment.ProviderReferenceId!, ct);
            payment.Status = PaymentStatus.Failed;
        }

        await db.SaveChangesAsync(ct);
    }
}
