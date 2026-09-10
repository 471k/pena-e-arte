using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.BoothRent.Queries;

public record GetBoothRentChargesQuery(Guid? ArtistId = null) : IRequest<List<BoothRentChargeResponse>>;

public class GetBoothRentChargesHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetBoothRentChargesQuery, List<BoothRentChargeResponse>>
{
    public async Task<List<BoothRentChargeResponse>> Handle(GetBoothRentChargesQuery query, CancellationToken ct)
    {
        IQueryable<BoothRentCharge> q = db.BoothRentCharges;

        if (currentUser.Role == "artist")
        {
            // An artist can see only their own charges — the resolved artist id, not any
            // caller-supplied filter, same self-scoping convention as GetAppointmentsHandler.
            Guid? myArtistId = await db.Artists
                .Where(a => a.UserId == currentUser.UserId)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);

            q = q.Where(c => c.ArtistId == myArtistId);
        }
        else if (query.ArtistId.HasValue)
        {
            q = q.Where(c => c.ArtistId == query.ArtistId.Value);
        }

        return await q
            .Include(c => c.Artist)
            .OrderByDescending(c => c.ChargedDate)
            .Select(c => Map(c, c.Artist.FirstName + " " + c.Artist.LastName))
            .ToListAsync(ct);
    }

    internal static BoothRentChargeResponse Map(BoothRentCharge c, string? artistName = null) => new(
        c.Id, c.StudioId, c.ArtistId, artistName, c.BoothRentScheduleId,
        c.Amount, c.ChargedDate, c.IsSettled, c.SettledAt, c.SettledNote, c.CreatedAt);
}
