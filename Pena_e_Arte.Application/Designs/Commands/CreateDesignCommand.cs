using MediatR;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Designs.Commands;

public record CreateDesignCommand(CreateDesignRequest Request) : IRequest<DesignResponse>;

public class CreateDesignHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<CreateDesignCommand, DesignResponse>
{
    public async Task<DesignResponse> Handle(CreateDesignCommand command, CancellationToken ct)
    {
        CreateDesignRequest req = command.Request;

        Design design = new()
        {
            StudioId    = tenant.StudioId,
            ClientId    = req.ClientId,
            ArtistId    = req.ArtistId,
            Title       = req.Title,
            Description = req.Description
        };

        db.Designs.Add(design);
        await db.SaveChangesAsync(ct);

        return Map(design);
    }

    internal static DesignResponse Map(Design d) =>
        new(d.Id, d.StudioId, d.ClientId, d.ArtistId, d.Title, d.Description, d.CreatedAt);
}
