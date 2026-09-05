using MediatR;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Appointments.Queries;

public record CheckSlotAvailabilityQuery(
    Guid? ArtistId,
    DateTime Date,
    int DurationMinutes)
    : IRequest<SlotAvailabilityResult>;

public record SlotAvailabilityResult(bool Available, string? Reason);

public class CheckSlotAvailabilityHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<CheckSlotAvailabilityQuery, SlotAvailabilityResult>
{
    public async Task<SlotAvailabilityResult> Handle(
        CheckSlotAvailabilityQuery query, CancellationToken ct)
    {
        if (query.ArtistId is null)
        {
            bool anyAvailable = await db.IsAnyArtistAvailableAsync(tenant.StudioId, query.Date, query.DurationMinutes, ct);
            return anyAvailable
                ? new SlotAvailabilityResult(true, null)
                : new SlotAvailabilityResult(false, "No artist is available at that time.");
        }

        (bool available, string? reason) = await db.CheckArtistSlotAvailabilityAsync(
            tenant.StudioId, query.ArtistId.Value, query.Date, query.DurationMinutes, ct);
        return new SlotAvailabilityResult(available, reason);
    }
}
