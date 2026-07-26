using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Artists.Queries;

public record ArtistScheduleEntryResponse(
    DayOfWeek DayOfWeek,
    TimeSpan StartTime,
    TimeSpan EndTime,
    bool IsAvailable);

public record ArtistTimeOffResponse(
    Guid Id,
    DateTime StartDate,
    DateTime EndDate,
    string Reason);

public record ArtistAvailabilityResponse(
    IReadOnlyList<ArtistScheduleEntryResponse> Schedule,
    IReadOnlyList<ArtistTimeOffResponse> TimeOff);

public record GetArtistScheduleQuery(Guid ArtistId) : IRequest<ArtistAvailabilityResponse>;

public class GetArtistScheduleHandler(IAppDbContext db)
    : IRequestHandler<GetArtistScheduleQuery, ArtistAvailabilityResponse>
{
    public async Task<ArtistAvailabilityResponse> Handle(GetArtistScheduleQuery query, CancellationToken ct)
    {
        bool exists = await db.Artists.AnyAsync(a => a.Id == query.ArtistId, ct);
        if (!exists) throw new NotFoundException("Artist", query.ArtistId);

        List<ArtistScheduleEntryResponse> schedule =
            await db.ArtistSchedules
                    .Where(s => s.ArtistId == query.ArtistId)
                    .OrderBy(s => s.DayOfWeek)
                    .Select(s => new ArtistScheduleEntryResponse(s.DayOfWeek, s.StartTime, s.EndTime, s.IsAvailable))
                    .ToListAsync(ct);

        List<ArtistTimeOffResponse> timeOff =
            await db.ArtistTimeOffs
                    .Where(t => t.ArtistId == query.ArtistId && t.EndDate >= DateTime.UtcNow.Date)
                    .OrderBy(t => t.StartDate)
                    .Select(t => new ArtistTimeOffResponse(t.Id, t.StartDate, t.EndDate, t.Reason))
                    .ToListAsync(ct);

        return new ArtistAvailabilityResponse(schedule, timeOff);
    }
}
