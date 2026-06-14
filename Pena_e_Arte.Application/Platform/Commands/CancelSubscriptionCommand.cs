using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Platform.Commands;

public record CancelSubscriptionCommand(Guid StudioId) : IRequest;

public class CancelSubscriptionHandler(
    IAppDbContext                      db,
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

        subscription.Status        = SubscriptionStatus.Cancelled;
        subscription.PendingPlanId = null;

        logger.LogInformation(
            "Subscription cancelled for studio {@StudioId} by issuer",
            studio.Id);

        await db.SaveChangesAsync(ct);
    }
}

public class CancelSubscriptionValidator : AbstractValidator<CancelSubscriptionCommand>
{
    public CancelSubscriptionValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
