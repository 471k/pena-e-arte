using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Artists.Queries;

/// <summary>
/// Which of an artist's booked sessions fall inside a time-off range (E04c, A06), so adding time off,
/// or removing it, shows what it concerns before it is committed. Read only: nothing here (or in
/// AddArtistTimeOffHandler / DeleteArtistTimeOffHandler) moves, cancels or edits an appointment.
///
/// Time off is stored as studio-local whole days, inclusive (ArtistTimeOff.StartDate/EndDate, the same
/// .Date normalisation AddArtistTimeOffHandler applies), and appointments as UTC instants. The range
/// becomes [local midnight of StartDate, local midnight after EndDate) converted to UTC in the studio's
/// timezone, and a session is affected when it OVERLAPS that window: Date &lt; windowEnd AND
/// EndDate &gt; windowStart. So a session that starts before the range and ends inside it counts, one
/// that starts inside and ends after it counts, and one that ends exactly at the range's first midnight
/// (or starts exactly at the midnight after its last day) does not. This is deliberately stricter than
/// the booking check (CheckArtistScheduleAsync), which looks only at the day a session starts.
///
/// Only Pending and Confirmed sessions count: a Cancelled, Completed or No-show session no longer puts
/// anyone in the artist's time.
///
/// Authorization is exactly AddArtistTimeOffHandler's: the artist row must exist (404), and an artist
/// caller may only ask about their own row (403); owners and admins may ask about any artist in the
/// studio (tenant filter).
/// </summary>
public record GetTimeOffAffectedSessionsQuery(Guid ArtistId, DateTime StartDate, DateTime EndDate)
    : IRequest<TimeOffAffectedSessionsResponse>;

public class GetTimeOffAffectedSessionsHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetTimeOffAffectedSessionsQuery, TimeOffAffectedSessionsResponse>
{
    private static readonly AppointmentStatus[] Booked = [AppointmentStatus.Pending, AppointmentStatus.Confirmed];

    public async Task<TimeOffAffectedSessionsResponse> Handle(GetTimeOffAffectedSessionsQuery query, CancellationToken ct)
    {
        Artist? artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == query.ArtistId, ct);
        if (artist is null) throw new NotFoundException("Artist", query.ArtistId);

        if (currentUser.Role == "artist" && artist.UserId != currentUser.UserId)
            throw new ForbiddenException();

        DateTime startDate = query.StartDate.Date;
        DateTime endDate = query.EndDate.Date;

        string? timezone = await db.Studios
            .Where(s => s.Id == artist.StudioId)
            .Select(s => s.Timezone)
            .FirstOrDefaultAsync(ct);

        (DateTime windowStart, DateTime windowEnd) = UtcWindow(startDate, endDate, timezone);

        List<TimeOffAffectedSessionResponse> sessions = await db.Appointments
            .AsNoTracking()
            .Where(a => a.ArtistId == query.ArtistId
                        && Booked.Contains(a.Status)
                        && a.Date < windowEnd
                        && a.EndDate > windowStart)
            .OrderBy(a => a.Date)
            .Select(a => new TimeOffAffectedSessionResponse(
                a.Id, a.Date, a.EndDate, a.Client.FirstName + " " + a.Client.LastName, a.Status.ToString()))
            .ToListAsync(ct);

        return new TimeOffAffectedSessionsResponse(query.ArtistId, startDate, endDate, sessions);
    }

    /// <summary>
    /// [local midnight of <paramref name="startDate"/>, local midnight after <paramref name="endDate"/>)
    /// as UTC instants in the studio's timezone. Public for the boundary tests.
    /// </summary>
    public static (DateTime Start, DateTime End) UtcWindow(DateTime startDate, DateTime endDate, string? timezone) =>
        (TimezoneUtils.StudioLocalToUtcForRules(startDate.Date, timezone),
         TimezoneUtils.StudioLocalToUtcForRules(endDate.Date.AddDays(1), timezone));
}

public class GetTimeOffAffectedSessionsValidator : AbstractValidator<GetTimeOffAffectedSessionsQuery>
{
    /// <summary>Bounds the read; time off longer than a year is not a real case.</summary>
    public const int MaxRangeDays = 366;

    public GetTimeOffAffectedSessionsValidator()
    {
        RuleFor(x => x.ArtistId).NotEmpty();
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.EndDate)
            .NotEmpty()
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("EndDate must be on or after StartDate.");
        RuleFor(x => x)
            .Must(x => (x.EndDate.Date - x.StartDate.Date).TotalDays <= MaxRangeDays)
            .WithMessage($"A time-off range can be at most {MaxRangeDays} days.");
    }
}
