using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Designs.Queries;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs.Commands;

public record MarkDesignAsCatalogItemCommand(Guid DesignId, MarkDesignAsCatalogItemRequest Request) : IRequest<DesignResponse>;

public class MarkDesignAsCatalogItemHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<MarkDesignAsCatalogItemCommand, DesignResponse>
{
    public async Task<DesignResponse> Handle(MarkDesignAsCatalogItemCommand command, CancellationToken ct)
    {
        MarkDesignAsCatalogItemRequest req = command.Request;

        Design design = await db.Designs
            .Include(d => d.Revisions).ThenInclude(r => r.Approval)
            .FirstOrDefaultAsync(d => d.Id == command.DesignId, ct)
            ?? throw new NotFoundException(nameof(Design), command.DesignId);

        if (currentUser.Role == "artist")
        {
            Guid? myArtistId = await db.Artists
                .Where(a => a.UserId == currentUser.UserId)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);
            if (myArtistId is null || design.ArtistId != myArtistId.Value)
                throw new NotFoundException(nameof(Design), command.DesignId);
        }

        if (req.IsCatalogItem)
        {
            // A design can't simultaneously belong to a specific client's approval thread and
            // be a reusable catalog template — reject rather than silently orphaning history
            // the client has already engaged with (an approved revision means real progress).
            bool hasApprovedRevision = design.Revisions.Any(r => r.Approval?.Status == DesignApprovalStatus.Approved);
            if (design.ClientId is not null && hasApprovedRevision)
                throw new BusinessRuleViolationException(
                    "This design already has an approved revision tied to a client and can't be converted into a catalog item. Create a new design for the catalog instead.");

            design.ClientId = null;
            design.Price = req.Price;
        }
        else
        {
            design.Price = null;
        }

        design.IsCatalogItem = req.IsCatalogItem;
        design.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        return GetDesignsHandler.Map(design);
    }
}
