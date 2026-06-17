using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs.Commands;

public record UploadDesignRevisionCommand(UploadDesignRevisionRequest Request) : IRequest<DesignRevisionResponse>;

public class UploadDesignRevisionHandler(
    IAppDbContext     db,
    ICurrentTenant    tenant,
    IRealtimeNotifier realtime,
    IJobScheduler     jobScheduler)
    : IRequestHandler<UploadDesignRevisionCommand, DesignRevisionResponse>
{
    private const int RevisionTimeoutDays = 14;

    public async Task<DesignRevisionResponse> Handle(UploadDesignRevisionCommand command, CancellationToken ct)
    {
        UploadDesignRevisionRequest req = command.Request;

        bool designExists = await db.Designs.AnyAsync(d => d.Id == req.DesignId, ct);
        if (!designExists) throw new NotFoundException(nameof(Design), req.DesignId);

        int nextVersion = await db.DesignRevisions.CountAsync(r => r.DesignId == req.DesignId, ct) + 1;

        DesignRevision revision = new()
        {
            StudioId      = tenant.StudioId,
            DesignId      = req.DesignId,
            VersionNumber = nextVersion,
            FileUrl       = req.FileUrl,
            Notes         = req.Notes,
            UploadedAt    = DateTime.UtcNow
        };

        db.DesignRevisions.Add(revision);
        await db.SaveChangesAsync(ct);

        jobScheduler.ScheduleDesignRevisionTimeout(
            revision.Id, DateTimeOffset.UtcNow.AddDays(RevisionTimeoutDays));

        DesignRevisionResponse response = Map(revision);
        await realtime.NotifyStudioAsync(tenant.StudioId, "DesignUploaded", response, ct);

        return response;
    }

    internal static DesignRevisionResponse Map(DesignRevision r, DesignApproval? approval = null) =>
        new(r.Id, r.DesignId, r.VersionNumber, r.FileUrl, r.Notes, r.UploadedAt,
            approval?.Status.ToString(), approval?.ClientNotes);
}
