using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Designs.Commands;

/// <summary>
/// The artist brings an earlier version back as the design's newest version (2026-11-06), usually after the
/// client asked for it (RequestDesignVersionAgainCommand). It is an ordinary upload of the same stored file
/// (the old revision row is untouched; deleting a revision never deletes its file), sent through
/// UploadDesignRevisionCommand so ownership, the review-window job, the realtime event and the version
/// numbering are exactly those of any upload. The client then reviews the new version.
/// </summary>
public record RestoreDesignRevisionCommand(Guid DesignId, Guid RevisionId) : IRequest<DesignRevisionResponse>;

public class RestoreDesignRevisionHandler(IAppDbContext db, ISender sender)
    : IRequestHandler<RestoreDesignRevisionCommand, DesignRevisionResponse>
{
    public async Task<DesignRevisionResponse> Handle(RestoreDesignRevisionCommand command, CancellationToken ct)
    {
        DesignRevision old = await db.DesignRevisions
            .FirstOrDefaultAsync(r => r.DesignId == command.DesignId && r.Id == command.RevisionId, ct)
            ?? throw new NotFoundException(nameof(DesignRevision), command.RevisionId);

        DesignRevision? latest = await DesignReviewState.LatestRevisionAsync(db, command.DesignId, ct);
        if (latest?.Id == old.Id)
            throw new BusinessRuleViolationException($"Version {old.VersionNumber} is already the latest version.");

        // Ownership (an artist only on their own design) is checked by the upload handler.
        return await sender.Send(new UploadDesignRevisionCommand(new UploadDesignRevisionRequest(
            command.DesignId, old.FileUrl, $"Version {old.VersionNumber}, brought back.")), ct);
    }
}

public class RestoreDesignRevisionValidator : AbstractValidator<RestoreDesignRevisionCommand>
{
    public RestoreDesignRevisionValidator()
    {
        RuleFor(x => x.DesignId).NotEmpty();
        RuleFor(x => x.RevisionId).NotEmpty();
    }
}
