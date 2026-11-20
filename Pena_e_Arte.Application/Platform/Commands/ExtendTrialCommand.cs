using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Platform.Commands;

public record ExtendTrialCommand(Guid StudioId, ExtendTrialRequest Request) : IRequest, IAuditableCommand
{
    public string AuditAction => AuditActions.StudioTrialExtended;
    public string AuditTargetType => AuditTargetTypes.Studio;
    public Guid AuditTargetId => StudioId;
    public Guid? AuditStudioId => StudioId;
}

public class ExtendTrialHandler(IAppDbContext db)
    : IRequestHandler<ExtendTrialCommand>
{
    public const string PastDueMessage =
        "This studio's subscription payment is overdue, so a trial extension wouldn't restore access. "
        + "Access comes back when the overdue payment is paid (the owner updates their billing details), "
        + "or when you record a cash payment with Activate on the studio's page.";

    public const string CancelledMessage =
        "This studio's subscription is cancelled, so a trial extension wouldn't restore access. "
        + "Access comes back only with an active subscription again, for example when you record a cash payment "
        + "with Activate on the studio's page.";

    public async Task Handle(ExtendTrialCommand command, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #5 — trial extension cross-tenant, AdminOnly. See architecture.md.
        Studio studio = await db.Studios
            .IgnoreQueryFilters()
            .Include(s => s.Subscription)
            .FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), command.StudioId);

        if (studio.Subscription?.Status == SubscriptionStatus.Active)
            throw new BusinessRuleViolationException(
                "Trial extension is not applicable to studios with an active subscription.");

        // Phi's decision (19 Nov): refuse rather than write a date that has no effect. A past-due studio
        // stays PastDue here, TenantMiddleware blocks PastDue whatever the trial date, and
        // GracePeriodEndJob skips anything not in GracePeriod. The message names the two moves that do
        // restore access. Exclude From Dunning is deliberately not offered as one: it only stops the
        // reminder emails (PastDueReminderJob), the studio stays blocked.
        if (studio.Subscription?.Status == SubscriptionStatus.PastDue)
            throw new BusinessRuleViolationException(PastDueMessage);

        // Phi's decision (20 Nov), Desktop's recommendation: Cancelled gets the same treatment. The handler would move
        // the date and leave the status Cancelled, and TenantMiddleware blocks Cancelled whatever the trial date.
        if (studio.Subscription?.Status == SubscriptionStatus.Cancelled)
            throw new BusinessRuleViolationException(CancelledMessage);

        int days = command.Request.AdditionalDays;

        studio.TrialExpiresAt = Extend(studio.TrialExpiresAt, days);

        if (studio.Subscription is not null)
        {
            DateTime extendedTrialExpiry = Extend(studio.Subscription.TrialExpiresAt ?? DateTime.UtcNow, days);
            studio.Subscription.TrialExpiresAt = extendedTrialExpiry;
            studio.Subscription.GracePeriodEnd = extendedTrialExpiry.AddDays(7);

            // A grace-period studio whose trial is extended goes back to trialing
            if (studio.Subscription.Status == SubscriptionStatus.GracePeriod)
                studio.Subscription.Status = SubscriptionStatus.Trialing;
        }

        await db.SaveChangesAsync(ct);
    }

    // Extend from the current expiry while the trial is still running; from now once expired
    private static DateTime Extend(DateTime current, int days) =>
        current > DateTime.UtcNow
            ? current.AddDays(days)
            : DateTime.UtcNow.AddDays(days);
}

public class ExtendTrialValidator : AbstractValidator<ExtendTrialCommand>
{
    public ExtendTrialValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
        RuleFor(x => x.Request.AdditionalDays).InclusiveBetween(1, 90);
    }
}
