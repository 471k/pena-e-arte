using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.BoothRent.Commands;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.BoothRent.Queries;

public record GetBoothRentSchedulesQuery(Guid? ArtistId = null) : IRequest<List<BoothRentScheduleResponse>>;

public class GetBoothRentSchedulesHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetBoothRentSchedulesQuery, List<BoothRentScheduleResponse>>
{
    public async Task<List<BoothRentScheduleResponse>> Handle(GetBoothRentSchedulesQuery query, CancellationToken ct)
    {
        IQueryable<BoothRentSchedule> q = db.BoothRentSchedules;

        if (currentUser.Role == "artist")
        {
            Guid? myArtistId = await db.Artists
                .Where(a => a.UserId == currentUser.UserId)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);

            q = q.Where(s => s.ArtistId == myArtistId);
        }
        else if (query.ArtistId.HasValue)
        {
            q = q.Where(s => s.ArtistId == query.ArtistId.Value);
        }

        return await q
            .Include(s => s.Artist)
            .OrderBy(s => s.Artist.FirstName)
            .Select(s => CreateBoothRentScheduleHandler.Map(s, s.Artist.FirstName + " " + s.Artist.LastName))
            .ToListAsync(ct);
    }
}
