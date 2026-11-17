using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Jobs;

/// <summary>
/// Scheduled once per trial (JobScheduler), not recurring, so the Hangfire success heartbeat only says
/// it ran; the outcome line says what it did. Whether it ran when it should have is a reconciliation
/// question (a trial past its end still Trialing), which nothing answers yet.
/// </summary>
public class TrialExpiryJob(AppDbContext db, ILogger<TrialExpiryJob> logger)
{
    public async Task ExecuteAsync(Guid studioId, CancellationToken ct = default)
    {
        string outcome = await ExpireAsync(studioId, ct);
        logger.LogInformation("TrialExpiryJob {StudioId}: {Outcome}", studioId, outcome);
    }

    private async Task<string> ExpireAsync(Guid studioId, CancellationToken ct)
    {
        var subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StudioId == studioId, ct);

        if (subscription is null) return "skipped, no subscription";
        if (subscription.Status != SubscriptionStatus.Trialing)
            return $"skipped, status is {subscription.Status}";

        subscription.Status = SubscriptionStatus.GracePeriod;
        await db.SaveChangesAsync(ct);
        return "trial ended, moved to GracePeriod";
    }
}
