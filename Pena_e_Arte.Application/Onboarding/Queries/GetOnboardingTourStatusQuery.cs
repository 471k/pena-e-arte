using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Onboarding.Queries;

public record GetOnboardingTourStatusQuery(string Role) : IRequest<OnboardingTourStatusResponse>;

public class GetOnboardingTourStatusHandler(IAppDbContext db, ICurrentUser user)
    : IRequestHandler<GetOnboardingTourStatusQuery, OnboardingTourStatusResponse>
{
    public async Task<OnboardingTourStatusResponse> Handle(GetOnboardingTourStatusQuery query, CancellationToken ct)
    {
        if (!string.Equals(query.Role, user.Role, StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException("You can only check onboarding status for your own role.");

        UserOnboardingState? state = await db.UserOnboardingStates
            .FirstOrDefaultAsync(s => s.UserId == user.UserId && s.Role == query.Role, ct);

        return new OnboardingTourStatusResponse(state?.HasCompletedTour ?? false);
    }
}
