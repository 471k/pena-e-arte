using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Public.Queries;

public record CheckPublicSlotAvailabilityQuery(string StudioSlug, Guid? ArtistId, DateTime Date, int DurationMinutes)
    : IRequest<SlotAvailabilityResult>;

public class CheckPublicSlotAvailabilityHandler(IAppDbContext db)
    : IRequestHandler<CheckPublicSlotAvailabilityQuery, SlotAvailabilityResult>
{
    public async Task<SlotAvailabilityResult> Handle(CheckPublicSlotAvailabilityQuery query, CancellationToken ct)
    {
        // Approved: public/anonymous studio-slug resolution — same predicate as GetPublicStudioHandler.
        Studio studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Slug == query.StudioSlug && s.IsActive && s.IsPublished, ct)
            ?? throw new NotFoundException(nameof(Studio), query.StudioSlug);

        if (query.ArtistId is null)
        {
            bool anyAvailable = await db.IsAnyArtistAvailableAsync(studio.Id, query.Date, query.DurationMinutes, ct);
            return anyAvailable
                ? new SlotAvailabilityResult(true, null)
                : new SlotAvailabilityResult(false, "No artist is available at that time.");
        }

        (bool available, string? reason) = await db.CheckArtistSlotAvailabilityAsync(
            studio.Id, query.ArtistId.Value, query.Date, query.DurationMinutes, ct);
        return new SlotAvailabilityResult(available, reason);
    }
}
