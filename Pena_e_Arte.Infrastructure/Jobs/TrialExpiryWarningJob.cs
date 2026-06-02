using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Jobs;

public class TrialExpiryWarningJob(INotificationService notifications, AppDbContext db)
{
    public async Task ExecuteAsync(Guid studioId, CancellationToken ct = default)
    {
        var studio = await db.Studios.FindAsync([studioId], ct);
        if (studio is null) return;

        // TODO: resolve owner email and send 48h trial expiry warning
    }
}
