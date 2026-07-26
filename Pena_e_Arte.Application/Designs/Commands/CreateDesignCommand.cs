using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs.Commands;

public record CreateDesignCommand(CreateDesignRequest Request) : IRequest<DesignResponse>;

public class CreateDesignHandler(IAppDbContext db, ICurrentTenant tenant, ICurrentUser currentUser)
    : IRequestHandler<CreateDesignCommand, DesignResponse>
{
    public async Task<DesignResponse> Handle(CreateDesignCommand command, CancellationToken ct)
    {
        CreateDesignRequest req = command.Request;

        Guid artistId = req.ArtistId;
        if (currentUser.Role == "artist")
        {
            // An artist can only ever create designs assigned to themselves — any
            // artistId supplied in the request is ignored rather than trusted.
            Guid? myArtistId = await db.Artists
                .Where(a => a.UserId == currentUser.UserId)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);
            if (myArtistId is null)
                throw new ForbiddenException();
            artistId = myArtistId.Value;
        }

        Design design = new()
        {
            StudioId = tenant.StudioId,
            ClientId = req.ClientId,
            ArtistId = artistId,
            Title = req.Title,
            Description = req.Description
        };

        db.Designs.Add(design);
        await db.SaveChangesAsync(ct);

        return Map(design);
    }

    internal static DesignResponse Map(Design d) =>
        new(d.Id, d.StudioId, d.ClientId, d.ArtistId, d.Title, d.Description, d.CreatedAt);
}
