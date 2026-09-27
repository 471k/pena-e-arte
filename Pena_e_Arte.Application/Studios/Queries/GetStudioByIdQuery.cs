using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Studios.Queries;

public record GetStudioByIdQuery(Guid StudioId) : IRequest<StudioResponse>;

public class GetStudioByIdHandler(IAppDbContext db)
    : IRequestHandler<GetStudioByIdQuery, StudioResponse>
{
    public async Task<StudioResponse> Handle(GetStudioByIdQuery query, CancellationToken ct)
    {
        // AdminOnly endpoint — IgnoreQueryFilters approved: usage #8 (cross-tenant read).
        // See architecture.md Approved Usages table.
        Studio studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == query.StudioId, ct)
            ?? throw new NotFoundException("Studio", query.StudioId);

        bool currencyLocked = await StudioCurrencyLock.IsLockedAsync(db, studio.Id, ct);

        return new StudioResponse(
            studio.Id, studio.Name, studio.Slug, studio.City,
            studio.Latitude, studio.Longitude,
            studio.ShowPlatformBranding,
            AllowBrandingRemoval: false,
            AllowApiAccess: false,
            studio.TrialExpiresAt, studio.CreatedAt, studio.IsActive,
            studio.SlugLockedAt, studio.PhoneNumber, studio.InstagramHandle, studio.Nipt,
            studio.IsSolo, studio.IsPublished, studio.Timezone,
            SubscriptionStatus: null, PastDueSince: null,
            AddressLine1: studio.AddressLine1, AddressLine2: studio.AddressLine2, PostalCode: studio.PostalCode,
            CountryCode: studio.CountryCode, Currency: studio.Currency, CurrencyLocked: currencyLocked);
    }
}
