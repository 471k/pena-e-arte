using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.Services;

namespace Pena_e_Arte.Application.Appointments.Queries;

/// <summary>
/// The staff appointment list (ArtistAndAbove): the schedule and the artist's Today view.
/// <paramref name="AssignedToMe"/> narrows it to the caller's own assigned sessions whatever their role,
/// so an owner who is also an artist sees their own work rather than the whole studio's (E04a, D2).
/// An artist caller always gets only their own sessions, with or without it.
/// Each row carries the DEC03 readiness summary, read for the whole list in one extra query by
/// AppointmentReadinessReader, the same derivation the client's reads use; the itemised, staff-worded
/// list is GET /appointments/{id}/preparation.
/// </summary>
public record GetAppointmentsQuery(DateTime? From, DateTime? To, Guid? ArtistId = null, bool AssignedToMe = false)
    : IRequest<List<AppointmentResponse>>;

public class GetAppointmentsHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetAppointmentsQuery, List<AppointmentResponse>>
{
    public async Task<List<AppointmentResponse>> Handle(GetAppointmentsQuery query, CancellationToken ct)
    {
        IQueryable<Appointment> q = db.Appointments;

        if (query.From.HasValue) q = q.Where(a => a.Date >= query.From.Value);
        if (query.To.HasValue) q = q.Where(a => a.Date <= query.To.Value);

        if (currentUser.Role == "artist" || query.AssignedToMe)
        {
            // Artists only ever see their own appointments — an explicit artistId from
            // the caller is ignored rather than trusted, since another artist's GUID
            // could otherwise be guessed. The same lookup serves an owner's own artist profile.
            Guid? myArtistId = await db.Artists
                .Where(a => a.UserId == currentUser.UserId)
                .Select(a => (Guid?)a.Id)
                .FirstOrDefaultAsync(ct);

            // No artist profile means no assigned sessions. Filtering on a null id would instead
            // match every unassigned request (ArtistId IS NULL).
            if (myArtistId is null) return [];

            q = q.Where(a => a.ArtistId == myArtistId);
        }
        else if (query.ArtistId.HasValue)
        {
            q = q.Where(a => a.ArtistId == query.ArtistId.Value);
        }

        List<AppointmentResponse> appointments = await q
            .Include(a => a.Attachments)
            .Include(a => a.Artist)
            .Include(a => a.Service)
            // The brief and placement the Today view shows; one join, not a query per row.
            .Include(a => a.Intake)
            .OrderBy(a => a.Date)
            .Select(a => CreateAppointmentHandler.Map(
                a,
                a.Client.FirstName + " " + a.Client.LastName,
                a.Artist != null ? a.Artist.FirstName + " " + a.Artist.LastName : null,
                a.Client.UserId,
                false))
            .ToListAsync(ct);

        Dictionary<Guid, ReadinessFacts> readiness = await AppointmentReadinessReader.LoadAsync(db, q, ct);

        return appointments
            .Select(a => a with
            {
                // Medical information stays off list reads (A03: health details never in list
                // previews). The staff appointment page, GET /appointments/{id}, still carries it.
                SafetyNotes = null,
                Readiness = readiness.TryGetValue(a.Id, out ReadinessFacts? facts)
                    ? AppointmentReadinessReader.Summarize(AppointmentReadiness.Derive(facts))
                    : null,
            })
            .ToList();
    }
}

public class GetAppointmentsValidator : AbstractValidator<GetAppointmentsQuery>
{
    public GetAppointmentsValidator()
    {
        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage("'to' must not be before 'from'.");
    }
}
