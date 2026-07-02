using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Instagram.Queries;

public record GetInstagramConnectionStatusQuery(Guid ArtistId) : IRequest<InstagramConnectionStatusResponse>;

public class GetInstagramConnectionStatusHandler(IAppDbContext db)
    : IRequestHandler<GetInstagramConnectionStatusQuery, InstagramConnectionStatusResponse>
{
    public async Task<InstagramConnectionStatusResponse> Handle(
        GetInstagramConnectionStatusQuery request, CancellationToken ct)
    {
        // Artists carries the tenant query filter — confirms ArtistId belongs to the
        // caller's studio before any cross-tenant-unfiltered Instagram table is read.
        bool exists = await db.Artists.AnyAsync(a => a.Id == request.ArtistId, ct);
        if (!exists) throw new NotFoundException("Artist", request.ArtistId);

        InstagramConnection? connection = await db.InstagramConnections
            .Where(c => c.ArtistId == request.ArtistId && c.IsActive)
            .FirstOrDefaultAsync(ct);

        if (connection is null)
            return new InstagramConnectionStatusResponse(false, null, null, 0);

        int postCount = await db.InstagramPosts
            .CountAsync(p => p.ArtistId == request.ArtistId, ct);

        return new InstagramConnectionStatusResponse(
            true, connection.Username, connection.LastSyncedAt, postCount);
    }
}
