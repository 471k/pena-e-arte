using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Jobs;

public class DesignRevisionTimeoutJob(
    IAppDbContext db, IRealtimeNotifier realtime, ILogger<DesignRevisionTimeoutJob> logger)
{
    public async Task ExecuteAsync(Guid revisionId, CancellationToken ct = default)
    {
        string outcome = await ExpireAsync(revisionId, ct);
        logger.LogInformation("DesignRevisionTimeoutJob {RevisionId}: {Outcome}", revisionId, outcome);
    }

    private async Task<string> ExpireAsync(Guid revisionId, CancellationToken ct)
    {
        DesignRevision? revision = await db.DesignRevisions
            .IgnoreQueryFilters()
            .Include(r => r.Approval)
            .FirstOrDefaultAsync(r => r.Id == revisionId && r.DeletedAt == null, ct);

        if (revision is null) return "skipped, revision not found or deleted";

        if (revision.Approval is { Status: DesignApprovalStatus.Approved or DesignApprovalStatus.ChangesRequested })
            return $"skipped, already {revision.Approval.Status}";

        if (revision.Approval is null)
        {
            db.DesignApprovals.Add(new DesignApproval
            {
                StudioId = revision.StudioId,
                DesignRevisionId = revision.Id,
                Status = DesignApprovalStatus.Expired,
                ReviewedAt = DateTime.UtcNow,
            });
        }
        else
        {
            revision.Approval.Status = DesignApprovalStatus.Expired;
            revision.Approval.ReviewedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        await realtime.NotifyStudioAsync(
            revision.StudioId, "DesignRevisionExpired",
            new { revisionId = revision.Id, designId = revision.DesignId }, ct);
        return "expired";
    }
}
