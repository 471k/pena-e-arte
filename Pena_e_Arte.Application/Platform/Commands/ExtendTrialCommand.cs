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

    public const string NoSubscriptionMessage =
        "This studio has no subscription record, so a trial extension wouldn't restore access: the platform "
        + "blocks a studio without one on every request. Recording a cash payment with Activate on the studio's "
        + "page creates the record and restores access.";

    public async Task Handle(ExtendTrialCommand command, CancellationToken ct)
    {
        // IgnoreQueryFilters approved: usage #5 — trial extension cross-tenant, AdminOnly. See architecture.md.
        Studio studio = await db.Studios
            .IgnoreQueryFilters()
            .Include(s => s.Subscription)
            .FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), command.StudioId);

        // NoSubscription (24 Nov, Desktop's instruction B): the extension used to move only Studio.TrialExpiresAt,
        // which access never reads. SubscriptionAccessService reads the Subscription row alone and treats a missing
        // row as data corruption, so TenantMiddleware answers 402 on every request whatever the trial date: the
        // extension reported success and granted nothing. Refused like PastDue and Cancelled. Cash activation is the
        // path that works, because ActivateSubscriptionManuallyHandler creates the row.
        Subscription subscription = studio.Subscription
            ?? throw new BusinessRuleViolationException(NoSubscriptionMessage);

        if (subscription.Status == SubscriptionStatus.Active)
            throw new BusinessRuleViolationException(
                "Trial extension is not applicable to studios with an active subscription.");

        // Phi's decision (19 Nov): refuse rather than write a date that has no effect. A past-due studio
        // stays PastDue here, TenantMiddleware blocks PastDue whatever the trial date, and
        // GracePeriodEndJob skips anything not in GracePeriod. The message names the two moves that do
        // restore access. Exclude From Dunning is deliberately not offered as one: it only stops the
        // reminder emails (PastDueReminderJob), the studio stays blocked.
        if (subscription.Status == SubscriptionStatus.PastDue)
            throw new BusinessRuleViolationException(PastDueMessage);

        // Phi's decision (20 Nov), Desktop's recommendation: Cancelled gets the same treatment. The handler would move
        // the date and leave the status Cancelled, and TenantMiddleware blocks Cancelled whatever the trial date.
        if (subscription.Status == SubscriptionStatus.Cancelled)
            throw new BusinessRuleViolationException(CancelledMessage);

        int days = command.Request.AdditionalDays;

        studio.TrialExpiresAt = Extend(studio.TrialExpiresAt, days);

        DateTime extendedTrialExpiry = Extend(subscription.TrialExpiresAt ?? DateTime.UtcNow, days);
        subscription.TrialExpiresAt = extendedTrialExpiry;
        subscription.GracePeriodEnd = extendedTrialExpiry.AddDays(7);

        // A grace-period studio whose trial is extended goes back to trialing
        if (subscription.Status == SubscriptionStatus.GracePeriod)
            subscription.Status = SubscriptionStatus.Trialing;

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
