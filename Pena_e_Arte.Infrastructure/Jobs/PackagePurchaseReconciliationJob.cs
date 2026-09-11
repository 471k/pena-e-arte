using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Same shape as GiftCardReconciliationJob — a small parallel job, not a branch inside
/// PaymentReconciliationJob (see that job's own doc comment for why). Confirms a not-yet-confirmed
/// package purchase (ConfirmedAt == null) once the provider hold succeeds, granting the purchased
/// SessionCount.
/// </summary>
public class PackagePurchaseReconciliationJob(IAppDbContext db, IPaymentProvider paymentProvider, ILogger<PackagePurchaseReconciliationJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        List<PackagePurchase> pending = await db.PackagePurchases
            .IgnoreQueryFilters()
            .Include(p => p.Package)
            .Where(p => p.ConfirmedAt == null
                     && p.ProviderReferenceId != ""
                     && p.DeletedAt == null)
            .ToListAsync(ct);

        foreach (PackagePurchase purchase in pending)
        {
            try
            {
                PaymentProviderStatus? status = await paymentProvider.GetStatusAsync(
                    purchase.StudioId, purchase.ProviderReferenceId, ct);
                if (status == PaymentProviderStatus.Captured)
                {
                    purchase.SessionsRemaining = purchase.Package.SessionCount;
                    purchase.ConfirmedAt = DateTime.UtcNow;
                    purchase.UpdatedAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One studio's provider failure must not abort reconciliation for every other
                // studio's package purchases in this batch — log and move on, picked up on the next run.
                logger.LogError(ex, "Failed to reconcile package purchase {PurchaseId} for studio {StudioId}.", purchase.Id, purchase.StudioId);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
