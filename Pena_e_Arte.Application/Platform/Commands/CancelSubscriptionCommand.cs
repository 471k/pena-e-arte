using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Billing;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Platform.Commands;

/// <summary>Admin override of the yearly-refund formula. Override is null for the default
/// (formula) path; AdminFull/AdminNone require a Reason (validator below) and are recorded in
/// the audit log's metadata — never silently swapped in.</summary>
public record CancelSubscriptionCommand(Guid StudioId, RefundRule? Override = null, string? OverrideReason = null)
    : IRequest, IAuditableCommand
{
    public string AuditAction => AuditActions.SubscriptionCancelledByAdmin;
    public string AuditTargetType => AuditTargetTypes.Subscription;
    public Guid AuditTargetId => StudioId;
    public Guid? AuditStudioId => StudioId;

    // Set by the handler before it returns — see CancelMySubscriptionCommand for why this is
    // safe (AuditLogBehavior reads these back off the same command instance after next(ct)).
    public decimal? ComputedRefundAmount { get; set; }
    public int? ComputedMonthsUsed { get; set; }
    public string? ComputedRule { get; set; }
}

public class CancelSubscriptionHandler(
    IAppDbContext db,
    IStripeBillingService stripe,
    ICurrentUser currentUser,
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
        // IgnoreQueryFilters approved: usage #7 — subscription cancellation cross-tenant, AdminOnly. See architecture.md.
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

        string? stripeId = subscription.StripeSubscriptionId;
        subscription.Status = SubscriptionStatus.Cancelled;
        subscription.PendingPlanId = null;

        // §2.1.C / §2.5 — yearly-cancellation-refund branch. Trialing/GracePeriod
        // subscriptions never have a paid yearly invoice, so the `invoice is not null` guard
        // below correctly makes Override a no-op for them (no invoice → no SubscriptionRefund
        // row at all, not even a zero one) — see §3 flag.
        if (subscription.BillingInterval == BillingInterval.Yearly && stripeId is not null)
        {
            SubscriptionInvoicePayment? invoice = await db.SubscriptionInvoicePayments
                .Where(p => p.SubscriptionId == subscription.Id)
                .OrderByDescending(p => p.PaidAt)
                .FirstOrDefaultAsync(ct);

            if (invoice is not null)
            {
                (decimal refundAmount, int? monthsUsed, RefundRule rule) = command.Override switch
                {
                    RefundRule.AdminFull => (invoice.AmountPaid, (int?)null, RefundRule.AdminFull),
                    RefundRule.AdminNone => (0m, (int?)null, RefundRule.AdminNone),
                    _ => YearlyRefundCalculator.QuoteFor(invoice, DateTime.UtcNow) is YearlyRefundQuote q
                        ? (q.RefundAmount, (int?)q.MonthsUsed, RefundRule.YearlyFormula)
                        : (0m, (int?)null, RefundRule.YearlyFormula),
                };

                await YearlyRefundIssuer.IssueAsync(db, stripe, subscription, invoice,
                    new YearlyRefundQuote(monthsUsed ?? 0, invoice.AmountPaid, invoice.MonthlyReferencePrice ?? 0m, refundAmount),
                    rule, currentUser.UserId, command.OverrideReason, logger, ct);

                command.ComputedRefundAmount = refundAmount;
                command.ComputedMonthsUsed = monthsUsed;
                command.ComputedRule = rule.ToString();
            }
            subscription.CurrentPeriodEnd = DateTime.UtcNow; // §2.1.C
        }

        logger.LogInformation(
            "Subscription cancelled for studio {@StudioId} by admin",
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
        RuleFor(x => x.Override).Must(o => o is null or RefundRule.AdminFull or RefundRule.AdminNone)
            .WithMessage("Override must be AdminFull or AdminNone.");
        RuleFor(x => x.OverrideReason).NotEmpty().When(x => x.Override is not null)
            .WithMessage("A reason is required when overriding the refund amount.");
    }
}
