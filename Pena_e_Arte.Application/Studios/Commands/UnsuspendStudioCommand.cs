using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Studios.Commands;

public record UnsuspendStudioCommand(Guid StudioId) : IRequest, IAuditableCommand
{
    public string AuditAction => AuditActions.StudioUnsuspended;
    public string AuditTargetType => AuditTargetTypes.Studio;
    public Guid AuditTargetId => StudioId;
    public Guid? AuditStudioId => StudioId;
}

public class UnsuspendStudioHandler(
    IAppDbContext db,
    ISubscriptionAccessService subscriptionAccess,
    IStripeBillingService stripe,
    ILogger<UnsuspendStudioHandler> logger)
    : IRequestHandler<UnsuspendStudioCommand>
{
    public async Task Handle(UnsuspendStudioCommand command, CancellationToken ct)
    {
        Domain.Entities.Studio studio = await db.Studios
            .Include(s => s.Subscription)
            .FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Studio), command.StudioId);

        studio.IsActive = true;
        await db.SaveChangesAsync(ct);
        await subscriptionAccess.InvalidateCacheAsync(command.StudioId, ct);

        // D6 — resume billing at the next normal renewal date; the suspended time is not
        // credited back. Best-effort, same pattern as CancelSubscriptionHandler.
        string? stripeSubscriptionId = studio.Subscription?.StripeSubscriptionId;
        if (stripeSubscriptionId is not null && studio.Subscription!.Status != SubscriptionStatus.Cancelled)
        {
            try
            {
                await stripe.ResumeCollectionAsync(stripeSubscriptionId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to resume Stripe collection for subscription {StripeSubscriptionId} "
                    + "(studio {StudioId}) — manual Stripe action required",
                    stripeSubscriptionId, studio.Id);
            }
        }
    }
}

public class UnsuspendStudioValidator : AbstractValidator<UnsuspendStudioCommand>
{
    public UnsuspendStudioValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
