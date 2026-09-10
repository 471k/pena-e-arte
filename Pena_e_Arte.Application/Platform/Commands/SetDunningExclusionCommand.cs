using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Platform.Commands;

public record SetDunningExclusionCommand(Guid StudioId, SetDunningExclusionRequest Request) : IRequest, IAuditableCommand
{
    public string AuditAction => AuditActions.SubscriptionDunningExclusionChanged;
    public string AuditTargetType => AuditTargetTypes.Subscription;
    public Guid AuditTargetId => StudioId;
    public Guid? AuditStudioId => StudioId;
}

public class SetDunningExclusionHandler(IAppDbContext db)
    : IRequestHandler<SetDunningExclusionCommand>
{
    public async Task Handle(SetDunningExclusionCommand command, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #5 — cross-tenant subscription admin action,
        // AdminOnly. See architecture.md and ExtendTrialCommand's identical precedent.
        Domain.Entities.Subscription subscription = await db.Subscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.StudioId == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Subscription), command.StudioId);

        subscription.DunningExcludedManually = command.Request.Excluded;

        await db.SaveChangesAsync(ct);
    }
}
