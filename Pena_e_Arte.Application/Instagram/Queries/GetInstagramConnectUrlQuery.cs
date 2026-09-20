using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Instagram.Queries;

public record GetInstagramConnectUrlQuery(Guid ArtistId) : IRequest<string>;

public class GetInstagramConnectUrlHandler(
    IAppDbContext db,
    IInstagramService instagram,
    IInstagramStateSigner stateSigner,
    ICurrentUser currentUser) : IRequestHandler<GetInstagramConnectUrlQuery, string>
{
    public async Task<string> Handle(GetInstagramConnectUrlQuery request, CancellationToken ct)
    {
        // db.Artists carries the tenant query filter, so a caller can never mint a
        // connect URL for an artist outside their own studio.
        bool exists = await db.Artists.AnyAsync(a => a.Id == request.ArtistId, ct);
        if (!exists) throw new NotFoundException("Artist", request.ArtistId);

        // The tenant filter scopes to the studio, not the person: an artist may only start the
        // connect flow for their own profile (whoever completes the consent screen binds their
        // Instagram to this artistId).
        await ArtistOwnershipGuard.EnsureCanActAsync(db, currentUser, request.ArtistId, ct);

        string state = stateSigner.Sign(request.ArtistId);
        return instagram.BuildAuthorizationUrl(state);
    }
}
