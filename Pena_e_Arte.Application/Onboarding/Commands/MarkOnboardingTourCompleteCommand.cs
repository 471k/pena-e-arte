using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Onboarding.Commands;

public record MarkOnboardingTourCompleteCommand(MarkOnboardingTourCompleteRequest Request) : IRequest;

public class MarkOnboardingTourCompleteHandler(IAppDbContext db, ICurrentUser user)
    : IRequestHandler<MarkOnboardingTourCompleteCommand>
{
    public async Task Handle(MarkOnboardingTourCompleteCommand command, CancellationToken ct)
    {
        if (!string.Equals(command.Request.Role, user.Role, StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException("You can only complete the onboarding tour for your own role.");

        UserOnboardingState? state = await db.UserOnboardingStates
            .FirstOrDefaultAsync(s => s.UserId == user.UserId && s.Role == command.Request.Role, ct);

        if (state is null)
        {
            state = UserOnboardingState.Create(user.UserId, command.Request.Role);
            db.UserOnboardingStates.Add(state);
        }

        state.MarkComplete();
        await db.SaveChangesAsync(ct);
    }
}
