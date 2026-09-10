using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Waitlists.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Waitlists.Queries;

public record GetWaitlistQuery(Guid? ArtistId = null) : IRequest<List<WaitlistEntryResponse>>;

public class GetWaitlistHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetWaitlistQuery, List<WaitlistEntryResponse>>
{
    public async Task<List<WaitlistEntryResponse>> Handle(GetWaitlistQuery query, CancellationToken ct)
    {
        IQueryable<Domain.Entities.Waitlist> q = db.WaitlistEntries;

        if (currentUser.Role == "artist")
        {
            // Artists see only their own studio-wide waitlist matches — same self-scoping
            // convention as GetAppointmentsHandler: an explicit artistId from the caller is
            // ignored in favor of the artist resolved from the JWT.
            Guid? myArtistId = await db.Artists
                .Where(a => a.UserId == currentUser.UserId)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);

            q = q.Where(w => w.ArtistId == myArtistId);
        }
        else if (query.ArtistId.HasValue)
        {
            q = q.Where(w => w.ArtistId == query.ArtistId.Value);
        }

        return await q
            .Include(w => w.Artist)
            .Include(w => w.Client)
            .OrderBy(w => w.CreatedAt)
            .Select(w => JoinWaitlistHandler.Map(
                w,
                w.Artist != null ? w.Artist.FirstName + " " + w.Artist.LastName : null,
                w.Client != null ? w.Client.FirstName + " " + w.Client.LastName : null))
            .ToListAsync(ct);
    }
}
