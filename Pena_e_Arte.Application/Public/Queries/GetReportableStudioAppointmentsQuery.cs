using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;

namespace Pena_e_Arte.Application.Public.Queries;

// Mirrors GetReviewableStudioAppointmentsQuery — see GetReportableArtistAppointmentsQuery for
// the two deliberate deltas (no Completed filter, no dedup exclusion).
public record GetReportableStudioAppointmentsQuery(string Slug, Guid ReporterUserId)
    : IRequest<List<ReportableAppointmentResponse>>;

public class GetReportableStudioAppointmentsHandler(IAppDbContext db)
    : IRequestHandler<GetReportableStudioAppointmentsQuery, List<ReportableAppointmentResponse>>
{
    public async Task<List<ReportableAppointmentResponse>> Handle(
        GetReportableStudioAppointmentsQuery query, CancellationToken ct)
    {
        // Approved: public portfolio lookup — see architecture.md AllowAnonymous Exceptions.
        Domain.Entities.Studio? studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Slug == query.Slug && s.IsActive, ct);

        if (studio is null) return [];

        return await ReportableAppointmentsQueryHelper.ToReportableAppointmentsAsync(
            db,
            db.Appointments.IgnoreQueryFilters().Where(a => a.StudioId == studio.Id),
            query.ReporterUserId,
            ct);
    }
}
