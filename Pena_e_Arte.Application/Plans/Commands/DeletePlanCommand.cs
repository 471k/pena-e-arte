using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Plans.Commands;

public record DeletePlanCommand(Guid PlanId) : IRequest;

public class DeletePlanHandler(IAppDbContext db)
    : IRequestHandler<DeletePlanCommand>
{
    public async Task Handle(DeletePlanCommand command, CancellationToken ct)
    {
        Domain.Entities.Plan plan = await db.Plans
            .FirstOrDefaultAsync(p => p.Id == command.PlanId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Plan), command.PlanId);

        bool hasActiveSubscriptions = await db.Subscriptions
            .AnyAsync(s => s.PlanId == command.PlanId, ct);

        if (hasActiveSubscriptions)
            throw new BusinessRuleViolationException(
                "Cannot delete a plan that has active subscriptions.");

        db.Plans.Remove(plan);
        await db.SaveChangesAsync(ct);
    }
}

public class DeletePlanValidator : AbstractValidator<DeletePlanCommand>
{
    public DeletePlanValidator()
    {
        RuleFor(x => x.PlanId).NotEmpty();
    }
}
