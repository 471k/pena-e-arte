using MediatR;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Public.Queries;

/// <summary>
/// C03 (slice 7): the guest booking page's slot list, the anonymous sibling of GetAvailableSlotsQuery, resolved by
/// published studio slug exactly as CheckPublicSlotAvailabilityQuery is. A preview, not a reservation.
/// </summary>
public record GetPublicAvailableSlotsQuery(
    string StudioSlug,
    Guid? ArtistId,
    DateOnly From,
    DateOnly To,
    int DurationMinutes)
    : IRequest<AvailableSlotsResult>;

public class GetPublicAvailableSlotsHandler(IAppDbContext db, TimeProvider clock)
    : IRequestHandler<GetPublicAvailableSlotsQuery, AvailableSlotsResult>
{
    public async Task<AvailableSlotsResult> Handle(GetPublicAvailableSlotsQuery query, CancellationToken ct)
    {
        // Approved: public/anonymous studio-slug resolution — shared via PublicStudioLookupExtensions.
        Studio studio = await db.GetPublishedStudioBySlugAsync(query.StudioSlug, ct)
            ?? throw new NotFoundException(nameof(Studio), query.StudioSlug);

        // A guest booking needs the same 30-minute lead as any booking (CreateGuestAppointmentValidator).
        DateTime earliest = clock.GetUtcNow().UtcDateTime.AddMinutes(BookingContentValidationRules.MinimumLeadMinutes);

        return await db.FindAvailableSlotsAsync(
            studio.Id, query.ArtistId, query.From, query.To, query.DurationMinutes,
            earliest, excludeAppointmentId: null, ct);
    }
}
