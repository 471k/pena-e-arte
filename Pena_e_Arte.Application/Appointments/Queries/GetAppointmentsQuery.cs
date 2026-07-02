using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Queries;

public record GetAppointmentsQuery(DateTime? From, DateTime? To, Guid? ArtistId = null) : IRequest<List<AppointmentResponse>>;

public class GetAppointmentsHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetAppointmentsQuery, List<AppointmentResponse>>
{
    public async Task<List<AppointmentResponse>> Handle(GetAppointmentsQuery query, CancellationToken ct)
    {
        IQueryable<Domain.Entities.Appointment> q = db.Appointments;

        if (query.From.HasValue) q = q.Where(a => a.Date >= query.From.Value);
        if (query.To.HasValue)   q = q.Where(a => a.Date <= query.To.Value);

        if (currentUser.Role == "artist")
        {
            // Artists only ever see their own appointments — an explicit artistId from
            // the caller is ignored rather than trusted, since another artist's GUID
            // could otherwise be guessed.
            Guid? myArtistId = await db.Artists
                .Where(a => a.UserId == currentUser.UserId)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);

            q = q.Where(a => a.ArtistId == myArtistId);
        }
        else if (query.ArtistId.HasValue)
        {
            q = q.Where(a => a.ArtistId == query.ArtistId.Value);
        }

        return await q
            .OrderBy(a => a.Date)
            .Select(a => CreateAppointmentHandler.Map(a, a.Client.FirstName + " " + a.Client.LastName))
            .ToListAsync(ct);
    }
}
