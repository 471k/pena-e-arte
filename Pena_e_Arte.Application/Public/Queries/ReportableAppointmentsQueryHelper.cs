using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Public.Queries;

/// <summary>
/// Shared Join/Where/OrderBy/Select shape for
/// <see cref="GetReportableArtistAppointmentsHandler"/> and
/// <see cref="GetReportableStudioAppointmentsHandler"/> — the two differ only in which FK the
/// caller has already filtered <paramref name="targetAppointments"/> on (ArtistId vs StudioId).
/// Deliberately not merged with the pre-existing `GetReviewable{Artist,Studio}Appointments`
/// query pair — those predate this feature and are out of scope for this dedup.
/// </summary>
internal static class ReportableAppointmentsQueryHelper
{
    public static Task<List<ReportableAppointmentResponse>> ToReportableAppointmentsAsync(
        IAppDbContext db, IQueryable<Appointment> targetAppointments, Guid reporterUserId, CancellationToken ct) =>
        targetAppointments
            .Join(db.Clients.IgnoreQueryFilters(),
                  a => a.ClientId,
                  c => c.Id,
                  (a, c) => new { Appointment = a, ClientUserId = c.UserId })
            .Where(x => x.ClientUserId == reporterUserId)
            .OrderByDescending(x => x.Appointment.Date)
            .Select(x => new ReportableAppointmentResponse(
                x.Appointment.Id, x.Appointment.Date, x.Appointment.DurationMinutes, x.Appointment.Status.ToString()))
            .ToListAsync(ct);
}
