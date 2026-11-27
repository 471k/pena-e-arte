using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Studios.Queries;

public record GetStudiosQuery : IRequest<List<StudioResponse>>;

public class GetStudiosHandler(IAppDbContext db)
    : IRequestHandler<GetStudiosQuery, List<StudioResponse>>
{
    public async Task<List<StudioResponse>> Handle(GetStudiosQuery query, CancellationToken ct)
    {
        List<Studio> studios = await db.Studios
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        return studios.Select(s => new StudioResponse(
            s.Id, s.Name, s.Slug, s.City,
            s.Latitude, s.Longitude,
            s.ShowPlatformBranding,
            AllowBrandingRemoval: false,
            AllowApiAccess: false,
            s.TrialExpiresAt, s.CreatedAt, s.IsActive,
            s.SlugLockedAt, s.PhoneNumber, s.InstagramHandle, s.Nipt,
            s.IsSolo, s.IsPublished, s.Timezone,
            SubscriptionStatus: null, PastDueSince: null,
            AddressLine1: s.AddressLine1, AddressLine2: s.AddressLine2, PostalCode: s.PostalCode,
            CountryCode: s.CountryCode, Currency: s.Currency,
            // NOT COMPUTED HERE, and false does NOT mean "not locked". The list used to run
            // StudioCurrencyLock.IsLockedAsync per row: four EXISTS queries per studio, which put
            // this endpoint over one second at about 20 studios (D08, measured 25 Nov 2026), to
            // fill a field none of the list's callers reads. The lock is read where it is used:
            // GET /studios/me and GET /studios/{id} still compute it (CurrencySettingsCard reads
            // it from /studios/me). If a list ever needs it, compute it set-based in one query,
            // never per row. Pinned by StudioListIntegrationTests.
            CurrencyLocked: false)).ToList();
    }
}
