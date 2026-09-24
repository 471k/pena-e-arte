using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Application.Platform.Revenue;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Billing.Commands;

/// <summary>Owner-initiated cancellation — replaces Stripe-portal cancellation entirely (A4).
/// Yearly: cancels immediately in Stripe, issues the formula refund against the latest paid
/// invoice. Monthly (card-billed): schedules cancel-at-period-end, no refund (unchanged
/// policy). Cash-billed: cancels immediately, no refund path (see §3 flag — a deliberate
/// simplification; zero real impact today, no paid studios).
///
/// Implements IAuditableCommand even though this is a tenant-scoped self-service action, not
/// an admin action — the audit log doubles as MrrRules' only precise-cancellation-timestamp
/// source pre-ledger (see MrrInputLoader.cs, and §2.1.C's AdminCancelledAt→CancelledAt
/// rename).</summary>
public record CancelMySubscriptionCommand(Guid StudioId)
    : IRequest<SubscriptionResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.SubscriptionCancelledByOwner;
    public string AuditTargetType => AuditTargetTypes.Subscription;
    public Guid AuditTargetId => StudioId;
    public Guid? AuditStudioId => StudioId;

    // Set by the handler before it returns — AuditLogBehavior calls AuditMetadataBuilder.Build
    // (request) AFTER next(ct) completes, reading these back off the same command instance.
    public decimal? ComputedRefundAmount { get; set; }
    public int? ComputedMonthsUsed { get; set; }
}

public class CancelMySubscriptionHandler(
    IAppDbContext db,
    IStripeBillingService billing,
    ICurrentUser currentUser,
    ISender sender,
    ILogger<CancelMySubscriptionHandler> logger)
    : IRequestHandler<CancelMySubscriptionCommand, SubscriptionResponse>
{
    public async Task<SubscriptionResponse> Handle(CancelMySubscriptionCommand command, CancellationToken ct)
    {
        Subscription subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StudioId == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Subscription), command.StudioId);

        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.PastDue))
            throw new BusinessRuleViolationException(
                $"A subscription with status '{subscription.Status}' cannot be cancelled this way.");

        DateTime now = DateTime.UtcNow;

        // A3 row 3 — release any scheduled downgrade first; the quote used the current period's invoice.
        if (subscription.PendingPlanId is not null && subscription.StripeSubscriptionId is not null)
        {
            await billing.CancelScheduledPriceChangeAsync(subscription.StripeSubscriptionId, ct);
            subscription.PendingPlanId = null;
            subscription.PendingBillingInterval = null;
        }

        decimal refundAmount = 0m;
        int? monthsUsed = null;

        if (subscription.BillingInterval == BillingInterval.Yearly && subscription.StripeSubscriptionId is not null)
        {
            SubscriptionInvoicePayment? invoice = await db.SubscriptionInvoicePayments
                .Where(p => p.SubscriptionId == subscription.Id)
                .OrderByDescending(p => p.PaidAt)
                .FirstOrDefaultAsync(ct);

            YearlyRefundQuote? quote = YearlyRefundCalculator.QuoteFor(invoice, now);
            if (quote is not null && invoice is not null)
            {
                refundAmount = quote.RefundAmount;
                monthsUsed = quote.MonthsUsed;
                await YearlyRefundIssuer.IssueAsync(
                    db, billing, subscription, invoice, quote, RefundRule.YearlyFormula,
                    currentUser.UserId, adminReason: null, logger, ct);
            }

            await billing.CancelSubscriptionAsync(subscription.StripeSubscriptionId, ct); // immediate, no proration
            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.CurrentPeriodEnd = now; // §2.1.C — precise access-end for MrrRules' pre-ledger window
            subscription.CancelAtPeriodEnd = false;
        }
        else if (subscription.StripeSubscriptionId is not null)
        {
            // Monthly, card-billed — unchanged policy (A3 row 2): cancel at period end, no refund.
            await billing.ScheduleCancellationAsync(subscription.StripeSubscriptionId, ct);
            subscription.CancelAtPeriodEnd = true;
        }
        else
        {
            // Cash-billed — see §3 flag: access ends immediately (no Stripe subscription
            // exists to schedule a period-end cancellation against, and there is no
            // background job that would later flip a "pending cash cancellation" to
            // Cancelled). Deliberate simplification, zero real impact today.
            subscription.Status = SubscriptionStatus.Cancelled;
            subscription.CurrentPeriodEnd = now;
        }

        command.ComputedRefundAmount = refundAmount;
        command.ComputedMonthsUsed = monthsUsed;

        await db.SaveChangesAsync(ct);
        await sender.Send(new SendSubscriptionCancelledNotificationCommand(
            subscription.Id, refundAmount, monthsUsed, subscription.Status, subscription.CurrentPeriodEnd), ct);

        return CreateSubscriptionHandler.Map(subscription);
    }
}

public class CancelMySubscriptionValidator : AbstractValidator<CancelMySubscriptionCommand>
{
    public CancelMySubscriptionValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}

public record KeepMySubscriptionCommand(Guid StudioId) : IRequest<SubscriptionResponse>;

/// <summary>Reverses a not-yet-effective scheduled cancellation ("Keep my plan" on a Monthly
/// subscription that's set to cancel at period end). Not IAuditableCommand — reversing a
/// not-yet-effective cancellation isn't the trust-sensitive event; CancelPlanChangeCommand
/// (the existing "undo a scheduled downgrade" command) isn't audited either, matching
/// precedent.</summary>
public class KeepMySubscriptionHandler(
    IAppDbContext db,
    IStripeBillingService billing,
    ILogger<KeepMySubscriptionHandler> logger)
    : IRequestHandler<KeepMySubscriptionCommand, SubscriptionResponse>
{
    public async Task<SubscriptionResponse> Handle(KeepMySubscriptionCommand command, CancellationToken ct)
    {
        Subscription subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StudioId == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Subscription), command.StudioId);

        if (!subscription.CancelAtPeriodEnd)
            throw new BusinessRuleViolationException("This subscription is not scheduled to cancel.");

        if (subscription.StripeSubscriptionId is not null)
            await billing.UndoScheduledCancellationAsync(subscription.StripeSubscriptionId, ct);

        subscription.CancelAtPeriodEnd = false;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Scheduled cancellation reversed for studio {@StudioId}", subscription.StudioId);

        return CreateSubscriptionHandler.Map(subscription);
    }
}

public class KeepMySubscriptionValidator : AbstractValidator<KeepMySubscriptionCommand>
{
    public KeepMySubscriptionValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
    }
}
