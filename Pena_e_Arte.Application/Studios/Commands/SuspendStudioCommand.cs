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

public record SuspendStudioCommand(Guid StudioId) : IRequest, IAuditableCommand
{
    public string AuditAction => AuditActions.StudioSuspended;
    public string AuditTargetType => AuditTargetTypes.Studio;
    public Guid AuditTargetId => StudioId;
    public Guid? AuditStudioId => StudioId;
}

public class SuspendStudioHandler(
    IAppDbContext db,
    ISubscriptionAccessService subscriptionAccess,
    IStripeBillingService stripe,
    ILogger<SuspendStudioHandler> logger)
    : IRequestHandler<SuspendStudioCommand>
{
    public async Task Handle(SuspendStudioCommand command, CancellationToken ct)
    {
        Domain.Entities.Studio studio = await db.Studios
            .Include(s => s.Subscription)
            .FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Domain.Entities.Studio), command.StudioId);

        studio.IsActive = false;
        await db.SaveChangesAsync(ct);
        await subscriptionAccess.InvalidateCacheAsync(command.StudioId, ct);

        // D6 — suspension blocks every login, so continuing to charge the card would bill a
        // studio that cannot use the product. Best-effort, same pattern as
        // CancelSubscriptionHandler: the DB change stands regardless of Stripe's outcome.
        string? stripeSubscriptionId = studio.Subscription?.StripeSubscriptionId;
        if (stripeSubscriptionId is not null && studio.Subscription!.Status != SubscriptionStatus.Cancelled)
        {
            try
            {
                await stripe.PauseCollectionAsync(stripeSubscriptionId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to pause Stripe collection for subscription {StripeSubscriptionId} "
                    + "(studio {StudioId}) — manual Stripe action required",
                    stripeSubscriptionId, studio.Id);
            }
        }
    }
}

public class SuspendStudioValidator : AbstractValidator<SuspendStudioCommand>
{
    public SuspendStudioValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
