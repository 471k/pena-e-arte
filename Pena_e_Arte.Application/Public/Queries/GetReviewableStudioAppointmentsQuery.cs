using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Public.Queries;

public record GetReviewableStudioAppointmentsQuery(string Slug, Guid AuthorUserId)
    : IRequest<List<ReviewableAppointmentResponse>>;

public class GetReviewableStudioAppointmentsHandler(IAppDbContext db)
    : IRequestHandler<GetReviewableStudioAppointmentsQuery, List<ReviewableAppointmentResponse>>
{
    public async Task<List<ReviewableAppointmentResponse>> Handle(
        GetReviewableStudioAppointmentsQuery query, CancellationToken ct)
    {
        // Approved: public portfolio lookup — see architecture.md AllowAnonymous Exceptions.
        Domain.Entities.Studio? studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Slug == query.Slug && s.IsActive, ct);

        if (studio is null) return [];

        // Approved: cross-tenant ownership check — same pattern as the verified-booking
        // join in GetStudioReviewsHandler (architecture.md IgnoreQueryFilters entry 20).
        return await db.Appointments
            .IgnoreQueryFilters()
            .Where(a => a.StudioId == studio.Id && a.Status == AppointmentStatus.Completed)
            .Join(db.Clients.IgnoreQueryFilters(),
                  a => a.ClientId,
                  c => c.Id,
                  (a, c) => new { Appointment = a, ClientUserId = c.UserId })
            .Where(x => x.ClientUserId == query.AuthorUserId)
            .Where(x => !db.Reviews.Any(r => r.AppointmentId == x.Appointment.Id && r.StudioId == studio.Id))
            .OrderByDescending(x => x.Appointment.Date)
            .Select(x => new ReviewableAppointmentResponse(
                x.Appointment.Id, x.Appointment.Date, x.Appointment.DurationMinutes))
            .ToListAsync(ct);
    }
}
