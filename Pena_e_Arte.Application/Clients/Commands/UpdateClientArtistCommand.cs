using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Clients.Commands;

public record UpdateClientArtistCommand(Guid ClientId, UpdateClientArtistRequest Request)
    : IRequest<ClientResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.ClientArtistReassigned;
    public string AuditTargetType => AuditTargetTypes.Client;
    public Guid AuditTargetId => ClientId;
}

public class UpdateClientArtistHandler(IAppDbContext db)
    : IRequestHandler<UpdateClientArtistCommand, ClientResponse>
{
    public async Task<ClientResponse> Handle(UpdateClientArtistCommand command, CancellationToken ct)
    {
        Client client = await db.Clients.FirstOrDefaultAsync(c => c.Id == command.ClientId, ct)
            ?? throw new NotFoundException(nameof(Client), command.ClientId);

        Artist? artist = command.Request.ArtistId is Guid artistId
            ? await CreateClientHandler.ResolveActiveArtistAsync(db, artistId, ct)
            : null;

        client.ArtistId = artist?.Id;
        client.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return CreateClientHandler.Map(client, artist);
    }
}
