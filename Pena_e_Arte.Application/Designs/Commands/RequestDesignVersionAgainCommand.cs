using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs.Commands;

/// <summary>
/// A client asks for an earlier version of their design back (Phi, 2026-11-06: "can a client go back to an
/// earlier version they preferred? yes"). No new entity: it is recorded as a "changes requested" review of
/// the design's LATEST version, whose feedback names the version wanted ("Asked for version 2 back."), sent
/// through ReviewDesignCommand so the artist gets the usual notification and the design moves to the
/// artist's queue. The artist then brings that version back as the newest one
/// (RestoreDesignRevisionCommand) and the client reviews it like any other version.
///
/// Only the design's own client may ask (404 for anyone else, never 403). Refused when the revision already
/// is the latest (review it instead) and when the latest is approved (the design is settled; the client
/// talks to the artist).
/// </summary>
public record RequestDesignVersionAgainCommand(Guid RevisionId, string? Notes) : IRequest<DesignRevisionResponse>;

public class RequestDesignVersionAgainHandler(IAppDbContext db, ICurrentUser currentUser, ISender sender)
    : IRequestHandler<RequestDesignVersionAgainCommand, DesignRevisionResponse>
{
    public async Task<DesignRevisionResponse> Handle(RequestDesignVersionAgainCommand command, CancellationToken ct)
    {
        DesignRevision wanted = await db.DesignRevisions
            .Include(r => r.Design)
            .FirstOrDefaultAsync(r => r.Id == command.RevisionId, ct)
            ?? throw new NotFoundException(nameof(DesignRevision), command.RevisionId);

        Client? me = currentUser.Role == "client" ? await db.FindClientForUserAsync(currentUser, ct) : null;
        if (me is null || me.Id != wanted.Design.ClientId)
            throw new NotFoundException(nameof(DesignRevision), command.RevisionId);

        DesignRevision latest = await DesignReviewState.LatestRevisionAsync(db, wanted.DesignId, ct)
            ?? throw new NotFoundException(nameof(DesignRevision), command.RevisionId);

        if (latest.Id == wanted.Id)
            throw new BusinessRuleViolationException(
                $"Version {wanted.VersionNumber} is already the latest version. Review it instead.");

        if (latest.Approval?.Status == DesignApprovalStatus.Approved)
            throw new BusinessRuleViolationException(
                $"Version {latest.VersionNumber} is approved, so the design is settled. Message your artist to change it.");

        string note = $"Asked for version {wanted.VersionNumber} back.";
        if (!string.IsNullOrWhiteSpace(command.Notes))
            note += " " + command.Notes.Trim();

        return await sender.Send(new ReviewDesignCommand(new ReviewDesignRequest(latest.Id, false, note)), ct);
    }
}

public class RequestDesignVersionAgainValidator : AbstractValidator<RequestDesignVersionAgainCommand>
{
    public RequestDesignVersionAgainValidator()
    {
        RuleFor(x => x.RevisionId).NotEmpty();
        // Leaves room for the "Asked for version N back." prefix inside the review's 2000-character limit.
        RuleFor(x => x.Notes).MaximumLength(1900).When(x => x.Notes is not null);
    }
}
