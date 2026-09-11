using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// A small parallel job rather than a GiftCard branch inside PaymentReconciliationJob:
/// PaymentReconciliationJob's three passes are written tightly around the Payment entity
/// specifically (its Include(p => p.Appointment), its Payment-only stale/hold-expiry checks) —
/// not generic enough to extend cleanly. GiftCard purchase has no appointment and no hold-expiry
/// concept (a gift card purchase is a one-time auth-hold-then-capture, not a slot reservation), so
/// this only needs the one "did the hold succeed" pass, same check
/// PaymentReconciliationJob.ReconcileCapturedAsync runs for Payment.
/// </summary>
public class GiftCardReconciliationJob(IAppDbContext db, IPaymentProvider paymentProvider, ILogger<GiftCardReconciliationJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        List<GiftCard> pending = await db.GiftCards
            .IgnoreQueryFilters()
            .Where(g => g.Status == GiftCardStatus.Pending
                     && g.ProviderReferenceId != null
                     && g.DeletedAt == null)
            .ToListAsync(ct);

        foreach (GiftCard giftCard in pending)
        {
            try
            {
                PaymentProviderStatus? status = await paymentProvider.GetStatusAsync(
                    giftCard.StudioId, giftCard.ProviderReferenceId!, ct);
                if (status == PaymentProviderStatus.Captured)
                {
                    giftCard.Status = GiftCardStatus.Active;
                    giftCard.UpdatedAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One studio's provider failure must not abort reconciliation for every other
                // studio's gift cards in this batch — log and move on, picked up on the next run.
                logger.LogError(ex, "Failed to reconcile gift card {GiftCardId} for studio {StudioId}.", giftCard.Id, giftCard.StudioId);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
