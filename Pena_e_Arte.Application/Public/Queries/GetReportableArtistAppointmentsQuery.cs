using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;

namespace Pena_e_Arte.Application.Public.Queries;

// Mirrors GetReviewableArtistAppointmentsQuery, with two deliberate deltas: no
// AppointmentStatus.Completed filter, and no "already reported" exclusion — a conduct report
// is about an incident that can occur during an appointment the studio controls the status of,
// gating on Completed would let a studio dodge every report by never marking an appointment
// complete, and a client may reasonably file more than one report against the same visit.
public record GetReportableArtistAppointmentsQuery(string Slug, Guid ReporterUserId)
    : IRequest<List<ReportableAppointmentResponse>>;

public class GetReportableArtistAppointmentsHandler(IAppDbContext db)
    : IRequestHandler<GetReportableArtistAppointmentsQuery, List<ReportableAppointmentResponse>>
{
    public async Task<List<ReportableAppointmentResponse>> Handle(
        GetReportableArtistAppointmentsQuery query, CancellationToken ct)
    {
        // Approved: public portfolio lookup — see architecture.md AllowAnonymous Exceptions.
        Domain.Entities.Artist? artist = await db.Artists
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Slug == query.Slug && a.DeletedAt == null, ct);

        if (artist is null) return [];

        // Approved: cross-tenant ownership check — same pattern as the verified-booking join
        // in GetReviewableArtistAppointmentsHandler, minus the Completed/dedup filters above.
        return await db.Appointments
            .IgnoreQueryFilters()
            .Where(a => a.ArtistId == artist.Id)
            .Join(db.Clients.IgnoreQueryFilters(),
                  a => a.ClientId,
                  c => c.Id,
                  (a, c) => new { Appointment = a, ClientUserId = c.UserId })
            .Where(x => x.ClientUserId == query.ReporterUserId)
            .OrderByDescending(x => x.Appointment.Date)
            .Select(x => new ReportableAppointmentResponse(
                x.Appointment.Id, x.Appointment.Date, x.Appointment.DurationMinutes, x.Appointment.Status.ToString()))
            .ToListAsync(ct);
    }
}
