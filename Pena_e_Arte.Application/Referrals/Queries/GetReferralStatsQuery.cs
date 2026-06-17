using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Referrals.Queries;

public record GetReferralStatsQuery(Guid StudioId) : IRequest<ReferralStatsResponse>;

public class GetReferralStatsHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetReferralStatsQuery, ReferralStatsResponse>
{
    public async Task<ReferralStatsResponse> Handle(GetReferralStatsQuery query, CancellationToken ct)
    {
        if (query.StudioId != tenant.StudioId)
            throw new NotFoundException(nameof(Studio), query.StudioId);

        ReferralCode? active = await db.ReferralCodes
            .Where(r => r.StudioId == query.StudioId && r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        // Aggregate redemptions across ALL codes for this studio so the count
        // does not reset to zero when a new code is generated.
        List<Guid> allCodeIds = await db.ReferralCodes
            .Where(r => r.StudioId == query.StudioId)
            .Select(r => r.Id)
            .ToListAsync(ct);

        List<ReferralRedemption> redemptions = await db.ReferralRedemptions
            .Where(r => allCodeIds.Contains(r.ReferralCodeId))
            .ToListAsync(ct);

        return new ReferralStatsResponse(
            active?.Code,
            redemptions.Count,
            redemptions.Count(r => r.DiscountApplied));
    }
}

public class GetReferralStatsValidator : AbstractValidator<GetReferralStatsQuery>
{
    public GetReferralStatsValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
