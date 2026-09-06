using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.Application.Platform.Queries;

public record GetPlatformReferralCodesQuery : IRequest<List<PlatformReferralCodeResponse>>;

public class GetPlatformReferralCodesHandler(IAppDbContext db)
    : IRequestHandler<GetPlatformReferralCodesQuery, List<PlatformReferralCodeResponse>>
{
    public async Task<List<PlatformReferralCodeResponse>> Handle(
        GetPlatformReferralCodesQuery query, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #6 — all referral codes cross-tenant, AdminOnly. See architecture.md.
        List<Domain.Entities.ReferralCode> codes = await db.ReferralCodes
            .IgnoreQueryFilters()
            .Include(r => r.Studio)
            .Include(r => r.Redemptions)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return codes.Select(r => new PlatformReferralCodeResponse(
            r.Id,
            r.StudioId,
            r.Studio.Name,
            r.Code,
            $"https://tattooos.co/register?ref={r.Code}",
            r.IsActive,
            r.IsSingleUse,
            r.CreatedAt,
            r.ExpiresAt,
            r.Redemptions.Count)).ToList();
    }
}
