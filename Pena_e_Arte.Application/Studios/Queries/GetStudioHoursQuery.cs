using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;

namespace Pena_e_Arte.Application.Studios.Queries;

public record StudioHoursEntryResponse(
    DayOfWeek DayOfWeek,
    TimeSpan StartTime,
    TimeSpan EndTime,
    bool IsOpen);

public record GetStudioHoursQuery(Guid StudioId) : IRequest<List<StudioHoursEntryResponse>>;

public class GetStudioHoursHandler(IAppDbContext db)
    : IRequestHandler<GetStudioHoursQuery, List<StudioHoursEntryResponse>>
{
    public async Task<List<StudioHoursEntryResponse>> Handle(
        GetStudioHoursQuery query, CancellationToken ct)
    {
        return await db.StudioHours
            .OrderBy(h => h.DayOfWeek)
            .Select(h => new StudioHoursEntryResponse(h.DayOfWeek, h.StartTime, h.EndTime, h.IsOpen))
            .ToListAsync(ct);
    }
}
