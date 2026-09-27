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

        List<StudioResponse> result = new(studios.Count);
        foreach (Studio s in studios)
        {
            // Admin studio list — not hot-path/paginated at any real scale today, so a per-row
            // lock check (four cheap EXISTS queries each) is simpler than hand-writing a
            // correlated-subquery projection for the same result.
            bool currencyLocked = await StudioCurrencyLock.IsLockedAsync(db, s.Id, ct);
            result.Add(new StudioResponse(
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
                CountryCode: s.CountryCode, Currency: s.Currency, CurrencyLocked: currencyLocked));
        }

        return result;
    }
}
