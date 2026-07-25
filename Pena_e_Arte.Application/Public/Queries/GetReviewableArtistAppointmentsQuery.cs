using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetReviewableArtistAppointmentsQuery(string Slug, Guid AuthorUserId)
    : IRequest<List<ReviewableAppointmentResponse>>;

public class GetReviewableArtistAppointmentsHandler(IAppDbContext db)
    : IRequestHandler<GetReviewableArtistAppointmentsQuery, List<ReviewableAppointmentResponse>>
{
    public async Task<List<ReviewableAppointmentResponse>> Handle(
        GetReviewableArtistAppointmentsQuery query, CancellationToken ct)
    {
        // Approved: public portfolio lookup — see architecture.md AllowAnonymous Exceptions.
        Domain.Entities.Artist? artist = await db.Artists
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Slug == query.Slug && a.DeletedAt == null, ct);

        if (artist is null) return [];

        // Approved: cross-tenant ownership check — same pattern as the verified-booking
        // join in GetArtistReviewsHandler (architecture.md IgnoreQueryFilters entry 19).
        return await db.Appointments
            .IgnoreQueryFilters()
            .Where(a => a.ArtistId == artist.Id && a.Status == AppointmentStatus.Completed)
            .Join(db.Clients.IgnoreQueryFilters(),
                  a => a.ClientId,
                  c => c.Id,
                  (a, c) => new { Appointment = a, ClientUserId = c.UserId })
            .Where(x => x.ClientUserId == query.AuthorUserId)
            .Where(x => !db.Reviews.Any(r => r.AppointmentId == x.Appointment.Id && r.ArtistId == artist.Id))
            .OrderByDescending(x => x.Appointment.Date)
            .Select(x => new ReviewableAppointmentResponse(
                x.Appointment.Id, x.Appointment.Date, x.Appointment.DurationMinutes))
            .ToListAsync(ct);
    }
}
