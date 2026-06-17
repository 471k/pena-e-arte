using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs.Commands;

public record ReviewDesignCommand(ReviewDesignRequest Request) : IRequest<DesignRevisionResponse>;

public class ReviewDesignHandler(
    IAppDbContext     db,
    ICurrentTenant    tenant,
    IRealtimeNotifier realtime,
    ISender           sender)
    : IRequestHandler<ReviewDesignCommand, DesignRevisionResponse>
{
    public async Task<DesignRevisionResponse> Handle(ReviewDesignCommand command, CancellationToken ct)
    {
        ReviewDesignRequest req = command.Request;

        DesignRevision revision = await db.DesignRevisions
            .Include(r => r.Approval)
            .FirstOrDefaultAsync(r => r.Id == req.DesignRevisionId, ct)
            ?? throw new NotFoundException(nameof(DesignRevision), req.DesignRevisionId);

        if (revision.Approval?.Status == DesignApprovalStatus.Approved)
            throw new DesignAlreadyApprovedException();

        DesignApprovalStatus newStatus = req.Approved
            ? DesignApprovalStatus.Approved
            : DesignApprovalStatus.ChangesRequested;

        DesignApproval approval;
        if (revision.Approval is null)
        {
            approval = new DesignApproval
            {
                StudioId         = tenant.StudioId,
                DesignRevisionId = revision.Id,
                Status           = newStatus,
                ClientNotes      = req.Notes,
                ReviewedAt       = DateTime.UtcNow
            };
            db.DesignApprovals.Add(approval);
        }
        else
        {
            revision.Approval.Status      = newStatus;
            revision.Approval.ClientNotes = req.Notes;
            revision.Approval.ReviewedAt  = DateTime.UtcNow;
            revision.Approval.UpdatedAt   = DateTime.UtcNow;
            approval = revision.Approval;
        }

        await db.SaveChangesAsync(ct);

        string eventName = req.Approved ? "DesignApproved" : "DesignChangeRequested";
        DesignRevisionResponse response = UploadDesignRevisionHandler.Map(revision, approval);
        await realtime.NotifyStudioAsync(tenant.StudioId, eventName, response, ct);

        await sender.Send(new SendDesignReviewNotificationCommand(req.DesignRevisionId, req.Approved), ct);

        return response;
    }
}
