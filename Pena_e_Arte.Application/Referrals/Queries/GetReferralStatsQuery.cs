using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Referrals.Queries;

public record GetReferralStatsQuery(Guid StudioId) : IRequest<ReferralStatsResponse>;

public class GetReferralStatsHandler(IAppDbContext db)
    : IRequestHandler<GetReferralStatsQuery, ReferralStatsResponse>
{
    public async Task<ReferralStatsResponse> Handle(GetReferralStatsQuery query, CancellationToken ct)
    {
        ReferralCode? active = await db.ReferralCodes
            .Where(r => r.StudioId == query.StudioId && r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (active is null)
            return new ReferralStatsResponse(null, 0, 0);

        List<ReferralRedemption> redemptions = await db.ReferralRedemptions
            .Where(r => r.ReferralCodeId == active.Id)
            .ToListAsync(ct);

        return new ReferralStatsResponse(
            active.Code,
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
