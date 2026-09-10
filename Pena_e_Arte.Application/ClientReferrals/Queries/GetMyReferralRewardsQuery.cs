using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.ClientReferrals.Queries;

/// <summary>Lists the caller's own unredeemed ClientReferralReward rows — earned credits
/// from someone else redeeming their referral code, spendable on their own next booking.</summary>
public record GetMyReferralRewardsQuery : IRequest<IReadOnlyList<ClientReferralRewardResponse>>;

public class GetMyReferralRewardsHandler(IAppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<GetMyReferralRewardsQuery, IReadOnlyList<ClientReferralRewardResponse>>
{
    public async Task<IReadOnlyList<ClientReferralRewardResponse>> Handle(
        GetMyReferralRewardsQuery query, CancellationToken ct)
    {
        Client client = await db.FindClientForUserAsync(currentUser, ct)
            ?? throw new NotFoundException(nameof(Client), currentUser.UserId);

        return await db.ClientReferralRewards
            .Where(r => r.ClientId == client.Id && !r.IsRedeemed)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ClientReferralRewardResponse(r.Id, r.RewardPercent, r.IsRedeemed, r.CreatedAt))
            .ToListAsync(ct);
    }
}
