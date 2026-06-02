using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Jobs;

public class GracePeriodEndJob(AppDbContext db)
{
    public async Task ExecuteAsync(Guid studioId, CancellationToken ct = default)
    {
        var subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StudioId == studioId, ct);

        if (subscription is null || subscription.Status != SubscriptionStatus.GracePeriod) return;

        subscription.Status = SubscriptionStatus.Cancelled;
        await db.SaveChangesAsync(ct);
    }
}
