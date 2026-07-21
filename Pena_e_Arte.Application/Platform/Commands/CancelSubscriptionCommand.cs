using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Platform.Commands;

public record CancelSubscriptionCommand(Guid StudioId) : IRequest, IAuditableCommand
{
    public string AuditAction     => AuditActions.SubscriptionCancelledByIssuer;
    public string AuditTargetType => AuditTargetTypes.Subscription;
    public Guid   AuditTargetId   => StudioId;
    public Guid?  AuditStudioId   => StudioId;
}

public class CancelSubscriptionHandler(
    IAppDbContext                      db,
    IStripeBillingService              stripe,
    ILogger<CancelSubscriptionHandler> logger)
    : IRequestHandler<CancelSubscriptionCommand>
{
    private static readonly HashSet<SubscriptionStatus> Cancellable =
    [
        SubscriptionStatus.Active,
        SubscriptionStatus.PastDue,
        SubscriptionStatus.Trialing,
        SubscriptionStatus.GracePeriod,
    ];

    public async Task Handle(CancelSubscriptionCommand command, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #7 — subscription cancellation cross-tenant, IssuerOnly. See architecture.md.
        Studio studio = await db.Studios
            .IgnoreQueryFilters()
            .Include(s => s.Subscription)
            .FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), command.StudioId);

        Subscription subscription = studio.Subscription
            ?? throw new BusinessRuleViolationException("Studio has no subscription to cancel.");

        if (!Cancellable.Contains(subscription.Status))
            throw new BusinessRuleViolationException(
                $"A subscription with status '{subscription.Status}' cannot be cancelled.");

        string? stripeId           = subscription.StripeSubscriptionId;
        subscription.Status        = SubscriptionStatus.Cancelled;
        subscription.PendingPlanId = null;

        logger.LogInformation(
            "Subscription cancelled for studio {@StudioId} by issuer",
            studio.Id);

        await db.SaveChangesAsync(ct);

        // Best-effort Stripe cancellation: if the subscription was created via Checkout,
        // cancel it in Stripe so the studio is not billed further.
        // DB record is already cancelled — a Stripe error must not surface to the caller.
        if (stripeId is not null)
        {
            try
            {
                await stripe.CancelSubscriptionAsync(stripeId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to cancel Stripe subscription {StripeSubscriptionId} for studio {StudioId} — " +
                    "local record already cancelled, manual Stripe cleanup may be required",
                    stripeId, studio.Id);
            }
        }
    }
}

public class CancelSubscriptionValidator : AbstractValidator<CancelSubscriptionCommand>
{
    public CancelSubscriptionValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
