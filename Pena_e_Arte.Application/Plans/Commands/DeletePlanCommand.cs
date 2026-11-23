using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Plans.Commands;

// Audited (D04 follow-up, 23 Nov): deleting a plan changes what every studio can buy, and afterwards the row is
// gone, so the name is kept in the entry's metadata.
public record DeletePlanCommand(Guid PlanId) : IRequest, IAuditableCommand
{
    public string AuditAction => AuditActions.PlanDeleted;
    public string AuditTargetType => AuditTargetTypes.Plan;
    public Guid AuditTargetId => PlanId;

    /// <summary>The deleted plan's name. Set by the handler; audit metadata only.</summary>
    public string? DeletedPlanName { get; set; }
}

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

        command.DeletedPlanName = plan.Name;
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
