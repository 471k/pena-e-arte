using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Services;

public class SubscriptionAccessService(AppDbContext db) : ISubscriptionAccessService
{
    public async Task<SubscriptionSnapshot?> GetSnapshotAsync(Guid studioId, CancellationToken ct = default) =>
        await db.Subscriptions
            .AsNoTracking()
            .Where(s => s.StudioId == studioId)
            .Select(s => new SubscriptionSnapshot(s.Status, s.TrialExpiresAt, s.GracePeriodEnd))
            .FirstOrDefaultAsync(ct);
}
