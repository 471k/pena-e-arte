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

/// <summary>The consequence the admin reviewed before confirming (slice 8 phase 3, D03), sent back with the cancel.
/// RefundAmount is the amount for the chosen rule; InvoiceId is null when no refund applies.</summary>
public record AdminCancellationConfirmation(string Status, Guid? InvoiceId, decimal RefundAmount);

/// <summary>Admin cancellation, optionally overriding the yearly-refund formula. Override is null for the formula, or
/// the name "AdminFull"/"AdminNone" (validated by name below, so an unknown value is a 422, not an endpoint exception);
/// an override requires a Reason and is recorded in the audit log's metadata. <see cref="Confirmed"/> is required: the
/// handler rebuilds the consequence and refuses with 409 when it no longer matches what the admin confirmed.</summary>
public record CancelSubscriptionCommand(
    Guid StudioId,
    string? Override = null,
    string? OverrideReason = null,
    AdminCancellationConfirmation? Confirmed = null)
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
    TimeProvider clock,
    ILogger<CancelSubscriptionHandler> logger)
    : IRequestHandler<CancelSubscriptionCommand>
{
    public const string QuoteChangedMessage =
        "The subscription or its refund changed since you opened this. Nothing was cancelled. Check what will happen now and confirm again.";

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

        if (!AdminCancellationQuotes.Cancellable.Contains(subscription.Status))
            throw new BusinessRuleViolationException(
                $"A subscription with status '{subscription.Status}' cannot be cancelled.");

        if (!AdminCancellationQuotes.TryParseOverride(command.Override, out RefundRule? overrideRule))
            throw new BusinessRuleViolationException("Override must be AdminFull or AdminNone.");

        // The consequence now, by the same rule as the quote the admin reviewed. Compared before anything changes, so a
        // refused cancel has cancelled nothing, refunded nothing and called no billing provider.
        DateTime now = clock.GetUtcNow().UtcDateTime;
        SubscriptionInvoicePayment? latestInvoice = await CancellationQuotes.LatestInvoiceAsync(db, subscription, ct);
        AdminCancellationOutcome outcome = AdminCancellationQuotes.Outcome(latestInvoice, overrideRule, now);

        if (command.Confirmed is not AdminCancellationConfirmation confirmed
            || confirmed.Status != subscription.Status.ToString()
            || confirmed.InvoiceId != outcome.Invoice?.Id
            || confirmed.RefundAmount != outcome.RefundAmount)
        {
            throw new CancellationQuoteChangedException(QuoteChangedMessage);
        }

        // Captured before any mutation — the contract fields survive the cancel, so this is the
        // MRR the Churn row removes. Only a subscription that was actually billing churns.
        SubscriptionStatus previousStatus = subscription.Status;
        decimal mrrBefore = MrrRules.MonthlyEquivalent(subscription);

        string? stripeId = subscription.StripeSubscriptionId;
        subscription.Status = SubscriptionStatus.Cancelled;
        subscription.PendingPlanId = null;

        // §2.1.C / §2.5 — yearly-cancellation-refund branch. Trialing/GracePeriod subscriptions
        // never have a paid yearly invoice, so no invoice makes the override a no-op for them (no
        // SubscriptionRefund row at all, not even a zero one) — see §3 flag.
        if (CancellationQuotes.HasRefundFormula(subscription))
        {
            if (outcome.Invoice is SubscriptionInvoicePayment invoice)
            {
                await YearlyRefundIssuer.IssueAsync(db, stripe, subscription, invoice,
                    new YearlyRefundQuote(outcome.MonthsUsed ?? 0, invoice.AmountPaid, invoice.MonthlyReferencePrice ?? 0m, outcome.RefundAmount),
                    outcome.Rule, currentUser.UserId, command.OverrideReason, logger, ct);

                command.ComputedRefundAmount = outcome.RefundAmount;
                command.ComputedMonthsUsed = outcome.MonthsUsed;
                command.ComputedRule = outcome.Rule.ToString();
            }
            subscription.CurrentPeriodEnd = now; // §2.1.C
        }

        if (previousStatus is SubscriptionStatus.Active or SubscriptionStatus.PastDue)
            RevenueEventRecorder.Record(db, subscription, mrrBefore, 0m, RevenueEventType.Churn,
                nameof(CancelSubscriptionHandler), stripeEventId: null);

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
        // Parsed here, not in the endpoint: an unknown value (or a number, which Enum.Parse would accept) is a 422.
        RuleFor(x => x.Override).Must(o => AdminCancellationQuotes.TryParseOverride(o, out _))
            .WithMessage("Override must be AdminFull or AdminNone.");
        RuleFor(x => x.OverrideReason).NotEmpty().When(x => x.Override is not null)
            .WithMessage("A reason is required when overriding the refund amount.");
        RuleFor(x => x.Confirmed).NotNull()
            .WithMessage("Review what the cancellation will do, then confirm it.");
        When(x => x.Confirmed is not null, () =>
        {
            RuleFor(x => x.Confirmed!.Status).NotEmpty();
            RuleFor(x => x.Confirmed!.RefundAmount).GreaterThanOrEqualTo(0m);
        });
    }
}
