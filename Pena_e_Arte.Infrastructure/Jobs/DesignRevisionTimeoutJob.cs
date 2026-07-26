using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Jobs;

public class DesignRevisionTimeoutJob(IAppDbContext db, IRealtimeNotifier realtime)
{
    public async Task ExecuteAsync(Guid revisionId, CancellationToken ct = default)
    {
        DesignRevision? revision = await db.DesignRevisions
            .IgnoreQueryFilters()
            .Include(r => r.Approval)
            .FirstOrDefaultAsync(r => r.Id == revisionId && r.DeletedAt == null, ct);

        if (revision is null) return;

        if (revision.Approval is { Status: DesignApprovalStatus.Approved or DesignApprovalStatus.ChangesRequested })
            return;

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
    }
}
