# Overnight Prompt — Yearly Cancellation and Refunds (Batch 3a)

> Feed this file directly to Claude Code as the task prompt. It is self-contained: exact
> files, exact current code (re-read from live source on 2026-09-24, on top of Batch 2b's
> merged commit `83782324`), exact target behaviour, exact tests, exact docs to sync. Read
> the whole file before writing anything.

**Date logged:** 2026-09-24
**Requested by:** Phi (Finance project, via Engineering Consultation on
`docs/claude/spec-yearly-refunds-and-revenue-ledger-2026-09-23.md`, Part A)
**Origin:** That spec left schema/migrations/webhook/Stripe mechanics open ("yours to
design; the financial rules below are final"). Section 2 below is the resulting design —
treat it as decided, not as a proposal to re-litigate. A1–A10 references below refer to
that spec's own section numbers.
**Prerequisite:** Batch 1 (`overnight-prompt-plan-tiers-yearly-2026-09-23.md`), Batch 2
(`overnight-prompt-mrr-reporting-fixes-2026-09-23.md`) and Batch 2b
(`overnight-prompt-mrr-billed-amount-snapshot-2026-09-23.md`) merged. Batch 2b's
`SubscriptionInvoicePayment` table (one row per paid Stripe invoice) is this batch's main
input — extended here, not redesigned.
**This is the launch blocker** — the spec's Part B (revenue ledger, MRR movements chart)
is a separate batch (`overnight-prompt-revenue-ledger-2026-09-24.md`) and can follow later.
**Mode:** Fully autonomous, no user present. Do not stop to ask questions — open product
questions are in §3 with defaults. Where this file states a fact about current code it was
re-read on 2026-09-24; if your checkout disagrees, trust your checkout, note the drift in
your final report, and do not silently change approach.

**Before starting**, run:

```bash
git add -A && git commit -m "checkpoint: before yearly cancellation refunds" --allow-empty
git checkout -b feature/yearly-cancellation-refunds
```

Commit at the end of each phase (§5–§10).

---

## 1. Goal

A studio that cancels a yearly plan today gets nothing back and (per the *monthly* policy
code path it currently shares) keeps access until `CurrentPeriodEnd` — neither matches the
refund terms Finance wants to publish. Build the exact refund formula in A1, a unified
owner-facing cancel flow that replaces Stripe-portal cancellation (A4), an admin override
path with a required reason (A5), a persisted refund record updated from Stripe's webhooks
(A6), and the admin "Refunds this month" figure (A7). Starter/Growth yearly stay unlinked
in Plan management until this ships (spec preamble) — Premium yearly is already
purchasable and is refunded by hand in the Stripe dashboard until this lands.

No studio is subscribed to a paid plan as of 2026-09-23 (confirmed in Batch 2's and Batch
2b's reports) — this batch ships against zero real financial exposure.

Applicable `CLAUDE.md` rules: #1 (tenant isolation — the new `SubscriptionRefund` entity is
deliberately unfiltered, same class as `Subscription`/`SubscriptionInvoicePayment`; see
§5.2), #2 (RBAC — every new endpoint gets `OwnerOnly`/`AdminOnly`), #3 (no PII in logs —
refund amounts/month counts are numbers, never names), #4 (secrets — nothing new), #5
(Serilog only), #6 (benchmark — §12), #7 (Help sync — **deliberately deferred this batch,
see §11's explicit rule-7 exception**).

---

## 2. Decisions made (Engineering Consultation) — implement as specified

### 2.1 Schema — one migration, three changes

**A. Three new nullable columns on `SubscriptionInvoicePayment`** (needed to compute A1's
formula — Batch 2b's row has `AmountPaid`/`DiscountAmount`/`Currency`/`PaidAt` but no period
start, no "what was Monthly worth at the time", and no refund target):

```csharp
public DateTime? PeriodStart { get; set; }            // this invoice's billing period start (Stripe invoice line item)
public decimal? MonthlyReferencePrice { get; set; }    // the tier's Monthly PlanPrice.Price at the moment this invoice paid — Yearly invoices only
public string? StripePaymentIntentId { get; set; }     // refund target
```

`SubscriptionInvoicePaymentConfiguration.cs` gets three new `builder.Property(...)` lines;
`MonthlyReferencePrice` needs `.HasPrecision(10, 2)` (mirror `AmountPaid`/`DiscountAmount`'s
existing precision). No new index — these are read by subscription id, not searched on
their own.

**B. New entity `SubscriptionRefund`** (A6's refund record):

```csharp
namespace Pena_e_Arte.Domain.Entities;

public class SubscriptionRefund
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SubscriptionId { get; set; }
    public Guid StudioId { get; set; }                      // denormalized — same convenience as SubscriptionInvoicePayment
    public Guid SubscriptionInvoicePaymentId { get; set; }   // the invoice this refund is against — one refund per invoice (idempotency)
    public string? StripeRefundId { get; set; }              // null only if the Stripe call itself failed before returning an id
    public string StripeInvoiceId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int MonthsUsed { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal MonthlyReferencePrice { get; set; }
    public RefundRule Rule { get; set; }
    public Guid InitiatedByUserId { get; set; }               // owner or admin user id
    public string? AdminReason { get; set; }                  // required when Rule is AdminFull/AdminNone (validator)
    public RefundStatus Status { get; set; } = RefundStatus.Pending;
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Subscription Subscription { get; set; } = null!;
}
```

New enums in `Pena_e_Arte.Domain/Enums/`:

```csharp
public enum RefundRule { YearlyFormula, AdminFull, AdminNone }
public enum RefundStatus { Pending, Succeeded, Failed }
```

Deliberately **not** a `TenantEntity` — same reasoning as `Subscription`/
`SubscriptionInvoicePayment` (admin needs a cross-tenant read for "Refunds this month", A7).
No new `IgnoreQueryFilters()` registry row is needed because none is called — reads go
through `db.SubscriptionRefunds` directly, same as `db.SubscriptionInvoicePayments` today.

`SubscriptionRefundConfiguration.cs` (mirror `SubscriptionInvoicePaymentConfiguration.cs`):

```csharp
builder.ToTable("subscription_refunds");
builder.HasKey(r => r.Id).HasName("pk_subscription_refunds");
builder.Property(r => r.Amount).HasPrecision(10, 2);
builder.Property(r => r.AmountPaid).HasPrecision(10, 2);
builder.Property(r => r.MonthlyReferencePrice).HasPrecision(10, 2);
// One refund per invoice — the DB-level half of A3's "exactly one Stripe refund" idempotency.
builder.HasIndex(r => r.SubscriptionInvoicePaymentId).IsUnique()
       .HasDatabaseName("ix_subscription_refunds_subscription_invoice_payment_id");
builder.HasIndex(r => r.StudioId).HasDatabaseName("ix_subscription_refunds_studio_id");
builder.HasOne(r => r.Subscription).WithMany().HasForeignKey(r => r.SubscriptionId)
       .HasConstraintName("fk_subscription_refunds_subscriptions").OnDelete(DeleteBehavior.Cascade);
```

Add `DbSet<SubscriptionRefund> SubscriptionRefunds { get; }` to `IAppDbContext`,
`AppDbContext`, `FakeDbContext` — identical three-file pattern Batch 2b used for
`SubscriptionInvoicePayments` (`Pena_e_Arte.Application/Persistence/IAppDbContext.cs:61`,
`Pena_e_Arte.Infrastructure/Persistence/AppDbContext.cs:68`,
`tests/Pena_e_Arte.UnitTests/Helpers/FakeDbContext.cs:47`).

Migration name: `AddYearlyCancellationRefunds`
(`dotnet ef migrations add AddYearlyCancellationRefunds --project Pena_e_Arte.Infrastructure`).

**C. A precise "when did this subscription actually stop billing" signal for owner
cancellations too, not just admin's.**

Today, `MrrInputLoader.cs` (`Pena_e_Arte.Application/Platform/Revenue/MrrInputLoader.cs:54-59`)
gets a precise cancellation timestamp *only* for admin cancellations, by looking up the
latest `AuditActions.SubscriptionCancelledByAdmin` audit row and passing it as
`SubscriptionRevenueInput.AdminCancelledAt` into `MrrRules.BillingWindow`
(`Pena_e_Arte.Application/Platform/Revenue/MrrRules.cs:77-78`). An owner-initiated yearly
cancellation now also ends access *immediately* (A3), not at `CurrentPeriodEnd` — so it
needs the same precise-end treatment, or `MrrRules`' pre-ledger D3 reconstruction (still the
only history source until Batch 3b ships) will keep counting a yearly-cancelled
subscription as billing all the way to its original 12-month mark.

Fix this at its root rather than special-casing two audit actions everywhere: rename
`SubscriptionRevenueInput.AdminCancelledAt` → `CancelledAt` (it's no longer admin-only), and
widen the audit-log lookup to include both actions:

```csharp
// MrrInputLoader.cs — was: a.Action == AuditActions.SubscriptionCancelledByAdmin
Dictionary<Guid, DateTime> cancelledAt = await db.AuditLogEntries
    .Where(a => (a.Action == AuditActions.SubscriptionCancelledByAdmin
                 || a.Action == AuditActions.SubscriptionCancelledByOwner)
                && a.TargetType == AuditTargetTypes.Subscription)
    .GroupBy(a => a.TargetId)
    .Select(g => new { StudioId = g.Key, At = g.Max(a => a.CreatedAt) })
    .ToDictionaryAsync(x => x.StudioId, x => x.At, ct);
```

Update `MrrRules.cs:77-78` (`input.AdminCancelledAt` → `input.CancelledAt`) and both
files' XML doc comments accordingly. New audit action:

```csharp
// AuditActions.cs, next to SubscriptionCancelledByAdmin
public const string SubscriptionCancelledByOwner = "Subscription.CancelledByOwner";
```

This is why the new owner-facing cancel command (§2.4) must implement `IAuditableCommand`
even though it's a tenant-scoped self-service action, not an admin action — the audit log
is doing double duty here as both the trust trail and `MrrRules`' only precise-timestamp
source pre-ledger. **Also set `subscription.CurrentPeriodEnd = now` on every yearly
cancellation** (owner and admin) at the moment `Status` flips to `Cancelled` — belt-and-
suspenders with the audit-log lookup: `BillingWindow`'s fallback (`s.CurrentPeriodEnd`,
`MrrRules.cs:78`) then reflects the true end even if the audit-log join is ever missed, and
neither path depends on the other being right.

### 2.2 `IStripeBillingService` — three new methods

```csharp
/// <summary>Sets cancel_at_period_end=true — the studio keeps access to CurrentPeriodEnd,
/// same as today's monthly-cancel-via-portal behaviour. Idempotent.</summary>
Task ScheduleCancellationAsync(string stripeSubscriptionId, CancellationToken ct);

/// <summary>Clears cancel_at_period_end — "Keep my plan". Idempotent.</summary>
Task UndoScheduledCancellationAsync(string stripeSubscriptionId, CancellationToken ct);

/// <summary>Refunds part or all of a payment (A1's computed amount, or an admin override).
/// amountInCents in integer cents (A1: "all arithmetic in integer cents"). Idempotent via
/// idempotencyKey — see A3's "idempotency key = subscription id + current period start".
/// Returns the Stripe refund id and its initial status ("succeeded"/"pending"/"failed").</summary>
Task<(string RefundId, string Status)> RefundAsync(
    string stripePaymentIntentId, long amountInCents, string idempotencyKey, CancellationToken ct);
```

Implementation in `StripeBillingService.cs` (constructor needs a new `RefundService
refundService` parameter, registered the same way the other `Stripe.*Service` params are —
check `Program.cs`'s Stripe DI registration block and add `RefundService` there the same way
`CustomerBalanceTransactionService`/`PriceService` etc. are already registered):

```csharp
public async Task ScheduleCancellationAsync(string stripeSubscriptionId, CancellationToken ct)
{
    SubscriptionUpdateOptions options = new() { CancelAtPeriodEnd = true };
    await subscriptionService.UpdateAsync(stripeSubscriptionId, options, null, ct);
}

public async Task UndoScheduledCancellationAsync(string stripeSubscriptionId, CancellationToken ct)
{
    SubscriptionUpdateOptions options = new() { CancelAtPeriodEnd = false };
    await subscriptionService.UpdateAsync(stripeSubscriptionId, options, null, ct);
}

public async Task<(string RefundId, string Status)> RefundAsync(
    string stripePaymentIntentId, long amountInCents, string idempotencyKey, CancellationToken ct)
{
    RefundCreateOptions options = new() { PaymentIntent = stripePaymentIntentId, Amount = amountInCents };
    RequestOptions requestOptions = new() { IdempotencyKey = idempotencyKey };
    Refund refund = await refundService.CreateAsync(options, requestOptions, ct);
    return (refund.Id, refund.Status);
}
```

**Verify against the compiled Stripe.net 52.4.1 SDK** (installed:
`Pena_e_Arte.Infrastructure.csproj:22`): `RefundCreateOptions.PaymentIntent` and
`Refund.Status` are the expected 52.x shapes, but this codebase's invoice webhook already
reads a newer, restructured shape (`invoice.Parent?.SubscriptionDetails?.SubscriptionId`,
`BillingEndpoints.cs:194` — the ~2025-03 "thin invoice" API redesign). Confirm the exact
compiled member names before writing §7's webhook code and note any drift in your report.

### 2.3 One refund definition, shared by the quote and both cancel paths

Same reasoning as `MrrRules`' "one MRR definition" — the quote an owner sees before
confirming must be *exactly* the amount actually refunded, computed once:

```csharp
namespace Pena_e_Arte.Application.Platform.Revenue; // co-located with MrrRules.cs

public sealed record YearlyRefundQuote(int MonthsUsed, decimal AmountPaid, decimal MonthlyReferencePrice, decimal RefundAmount);

public static class YearlyRefundCalculator
{
    /// <summary>A started month counts in full — count by monthly anniversaries of the
    /// period start (A1). .NET AddMonths already clamps month-end dates the way A1
    /// requires (31 Jan → 28/29 Feb) — no extra clamping logic needed.</summary>
    public static int MonthsUsed(DateTime periodStart, DateTime cancelledAt)
    {
        int months = 0;
        while (periodStart.AddMonths(months + 1) <= cancelledAt) months++;
        return months + 1; // cancelling on day 1 of the period = 1 month used
    }

    public static decimal Compute(decimal amountPaid, int monthsUsed, decimal monthlyReferencePrice) =>
        Math.Max(0m, amountPaid - monthsUsed * monthlyReferencePrice);

    /// <summary>Null when there is no billed invoice to refund against (no payment yet, or
    /// a pre-Batch-2b invoice with no PeriodStart snapshot).</summary>
    public static YearlyRefundQuote? QuoteFor(SubscriptionInvoicePayment? latestInvoice, DateTime now)
    {
        if (latestInvoice?.PeriodStart is not DateTime periodStart) return null;
        int months = MonthsUsed(periodStart, now);
        decimal monthlyRef = latestInvoice.MonthlyReferencePrice ?? 0m;
        decimal refund = Compute(latestInvoice.AmountPaid, months, monthlyRef);
        return new YearlyRefundQuote(months, latestInvoice.AmountPaid, monthlyRef, refund);
    }
}
```

Verify against A2's worked table before moving on (all four rows derive from
`monthly = yearly / 10` given the tiers' actual prices — Starter 290/29, Growth 590/59,
Premium 790/79 — confirm these are still `PlanPrice` seed values for the Monthly rows and
that `MonthsUsed`/`Compute` reproduce all sixteen cells exactly):

| Tier (paid) | Month 1 | Month 3 | Month 6 | Month 10+ |
|---|---|---|---|---|
| Starter (290, monthly 29) | 261 | 203 | 116 | 0 |
| Growth (590, monthly 59) | 531 | 413 | 236 | 0 |
| Premium (790, monthly 79) | 711 | 553 | 316 | 0 |
| Growth referred (paid 531, monthly 59) | 472 | 354 | 177 | 0 |

### 2.4 Owner cancel flow — replaces portal-based cancellation entirely

`BillingPage.tsx` today has no in-app cancel action; the "Manage billing" button
(`BillingPage.tsx:383`) opens a Stripe Customer Portal session
(`CreatePortalSessionAsync`, `StripeBillingService.cs:169-180`, no `configuration` passed —
Stripe's dashboard-default portal, which lets the owner cancel with no refund logic at all).
A4 requires turning portal cancellation off. **Decision: do this via the Stripe Dashboard
customer-portal configuration, not a new API-managed `Configuration` object** — the simpler
of the two options the spec allows ("a dedicated portal configuration created via API and
passed on every session, or the dashboard setting; your choice"), since it needs setting
exactly once per Stripe mode and A8's own launch checklist already has "confirm portal
cancellation is off in live mode" as a manual step. Do this now, in test mode, as part of
this phase (§9): Stripe Dashboard → Settings → Billing → Customer portal → uncheck
"Customers can cancel subscriptions". No code change to `CreatePortalSessionAsync`. The
"Manage billing" button stays — it now only reaches payment-method/invoice management,
which is what A4 says to keep in the portal.

New backend surface, `Pena_e_Arte.Application/Billing/Commands/` and `.../Queries/`:

```csharp
public record GetCancellationQuoteQuery : IRequest<CancellationQuoteResponse>;

public class GetCancellationQuoteHandler(IAppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<GetCancellationQuoteQuery, CancellationQuoteResponse>
{
    public async Task<CancellationQuoteResponse> Handle(GetCancellationQuoteQuery query, CancellationToken ct)
    {
        Subscription subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StudioId == tenant.StudioId, ct)
            ?? throw new NotFoundException(nameof(Subscription), tenant.StudioId);

        if (subscription.BillingInterval != BillingInterval.Yearly || subscription.StripeSubscriptionId is null)
            return new CancellationQuoteResponse("Monthly", 0m, null, subscription.CurrentPeriodEnd, null, null);

        SubscriptionInvoicePayment? invoice = await db.SubscriptionInvoicePayments
            .Where(p => p.SubscriptionId == subscription.Id)
            .OrderByDescending(p => p.PaidAt)
            .FirstOrDefaultAsync(ct);

        YearlyRefundQuote? quote = YearlyRefundCalculator.QuoteFor(invoice, DateTime.UtcNow);
        return quote is null
            ? new CancellationQuoteResponse("Yearly", 0m, null, DateTime.UtcNow, null, null)
            : new CancellationQuoteResponse(
                "Yearly", quote.RefundAmount, quote.MonthsUsed, DateTime.UtcNow, quote.AmountPaid, quote.MonthlyReferencePrice);
    }
}
```

`CancellationQuoteResponse(string BillingInterval, decimal RefundAmount, int? MonthsUsed,
DateTime AccessEndDate, decimal? AmountPaid, decimal? MonthlyReferencePrice)` in
`Pena_e_Arte.Contracts/Responses/`.

```csharp
public record CancelMySubscriptionCommand(Guid StudioId)
    : IRequest<SubscriptionResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.SubscriptionCancelledByOwner;
    public string AuditTargetType => AuditTargetTypes.Subscription;
    public Guid AuditTargetId => StudioId;
    public Guid? AuditStudioId => StudioId;

    // Set by the handler before it returns — AuditLogBehavior.cs calls
    // AuditMetadataBuilder.Build(request) AFTER next(ct) completes (see AuditLogBehavior.cs:26),
    // reading these back off the same command instance. Same mechanism §2.5 reuses for the
    // admin path — see that section for why this is safe and matches existing precedent.
    public decimal? ComputedRefundAmount { get; set; }
    public int? ComputedMonthsUsed { get; set; }
}
```

Handler (`CancelMySubscriptionHandler`, DI: `IAppDbContext db, IStripeBillingService
billing, ICurrentUser currentUser, ISender sender, ILogger<CancelMySubscriptionHandler>
logger`):

```csharp
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
            await IssueRefundAsync(db, billing, subscription, invoice, quote, RefundRule.YearlyFormula,
                currentUser.UserId, adminReason: null, ct); // §2.6 — shared helper, also used by §2.5
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
        // Cash-billed — see §3 flag.
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
```

```csharp
public record KeepMySubscriptionCommand(Guid StudioId) : IRequest<SubscriptionResponse>;
```

Handler: only meaningful when `CancelAtPeriodEnd` is true — clear it, call
`billing.UndoScheduledCancellationAsync` when `StripeSubscriptionId is not null`. Not
`IAuditableCommand` — reversing a not-yet-effective cancellation isn't the trust-sensitive
event; `CancelPlanChangeCommand` (the existing "undo a scheduled downgrade" command) isn't
audited either, matching precedent.

Endpoints, `BillingEndpoints.cs` (inside the existing `billingGroup`, next to
`/subscription/plan/pending`):

```csharp
billingGroup.MapGet("/subscription/cancel/quote", GetCancellationQuote).RequireAuthorization("OwnerOnly");
billingGroup.MapPost("/subscription/cancel", CancelMySubscription).RequireAuthorization("OwnerOnly");
billingGroup.MapDelete("/subscription/cancel", KeepMySubscription).RequireAuthorization("OwnerOnly");
```

Handler methods inject `ICurrentTenant tenant` and build `new
CancelMySubscriptionCommand(tenant.StudioId)` — identical pattern to `FinalizeCheckout`
(`BillingEndpoints.cs:118-129`, which already injects `ICurrentTenant tenant` for the same
reason).

### 2.5 Admin override flow

Extend the existing `CancelSubscriptionCommand`/`CancelSubscriptionHandler`
(`Pena_e_Arte.Application/Platform/Commands/CancelSubscriptionCommand.cs`, unchanged
signature today: `CancelSubscriptionCommand(Guid StudioId)`):

```csharp
public record CancelSubscriptionCommand(Guid StudioId, RefundRule? Override = null, string? OverrideReason = null)
    : IRequest, IAuditableCommand
{
    public string AuditAction => AuditActions.SubscriptionCancelledByAdmin;
    public string AuditTargetType => AuditTargetTypes.Subscription;
    public Guid AuditTargetId => StudioId;
    public Guid? AuditStudioId => StudioId;

    public decimal? ComputedRefundAmount { get; set; }
    public int? ComputedMonthsUsed { get; set; }
    public string? ComputedRule { get; set; }
}
```

Validator addition:

```csharp
RuleFor(x => x.Override).Must(o => o is null or RefundRule.AdminFull or RefundRule.AdminNone)
    .WithMessage("Override must be AdminFull or AdminNone.");
RuleFor(x => x.OverrideReason).NotEmpty().When(x => x.Override is not null)
    .WithMessage("A reason is required when overriding the refund amount.");
```

Handler: insert the same yearly-refund branch as §2.4's `Handle`, right after the existing
`subscription.Status = SubscriptionStatus.Cancelled; subscription.PendingPlanId = null;`
lines (`CancelSubscriptionCommand.cs:53-54`) and before `await db.SaveChangesAsync(ct)`
(line 60):

```csharp
if (subscription.BillingInterval == BillingInterval.Yearly && subscription.StripeSubscriptionId is not null)
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

        await IssueRefundAsync(db, stripe, subscription, invoice,
            new YearlyRefundQuote(monthsUsed ?? 0, invoice.AmountPaid, invoice.MonthlyReferencePrice ?? 0m, refundAmount),
            rule, currentUser.UserId, command.OverrideReason, ct);

        command.ComputedRefundAmount = refundAmount;
        command.ComputedMonthsUsed = monthsUsed;
        command.ComputedRule = rule.ToString();
    }
    subscription.CurrentPeriodEnd = DateTime.UtcNow; // §2.1.C
}
```

Add `ICurrentUser currentUser` to `CancelSubscriptionHandler`'s constructor (not injected
today). `AuditMetadataBuilder.cs` gets two new cases (amounts/counts only — rule #3):

```csharp
CancelSubscriptionCommand c => new Dictionary<string, object?>
{
    ["rule"] = c.ComputedRule,
    ["amount"] = c.ComputedRefundAmount,
    ["monthsUsed"] = c.ComputedMonthsUsed,
    ["reason"] = c.OverrideReason,
},
CancelMySubscriptionCommand c => new Dictionary<string, object?>
{
    ["amount"] = c.ComputedRefundAmount,
    ["monthsUsed"] = c.ComputedMonthsUsed,
},
```

`AdminNone` still writes a `SubscriptionRefund` row (Amount 0, Status Succeeded, no Stripe
call) — A6 says persist "every refund"; a zero-amount row is the one place that records the
admin's "no refund" decision against the invoice, not just the audit log's free-text reason.

Contracts: `CancelSubscriptionRequest(string? Override, string? Reason)` in
`Pena_e_Arte.Contracts/Requests/`. Endpoint (`PlatformEndpoints.cs:143-150`):

```csharp
private static async Task<IResult> CancelSubscription(
    Guid studioId, CancelSubscriptionRequest? request, ISender mediator, CancellationToken ct)
{
    RefundRule? overrideRule = request?.Override is string o ? Enum.Parse<RefundRule>(o, ignoreCase: true) : null;
    await mediator.Send(new CancelSubscriptionCommand(studioId, overrideRule, request?.Reason), ct);
    return Results.NoContent();
}
```

### 2.6 Shared refund-issuing helper

Both §2.4 and §2.5 need identical Stripe-call + `SubscriptionRefund`-row + idempotency
logic. Put it once, e.g. as an internal static method on `CancelSubscriptionHandler` called
from both handlers via a small shared static class
`Pena_e_Arte.Application/Billing/YearlyRefundIssuer.cs` (avoids a circular
Application-layer dependency between `Platform.Commands` and `Billing.Commands`):

```csharp
public static class YearlyRefundIssuer
{
    public static async Task IssueAsync(
        IAppDbContext db, IStripeBillingService billing, Subscription subscription,
        SubscriptionInvoicePayment invoice, YearlyRefundQuote quote, RefundRule rule,
        Guid initiatedByUserId, string? adminReason, ILogger logger, CancellationToken ct)
    {
        // A3 — "cancel sent twice / network retry: exactly one Stripe refund".
        bool alreadyRefunded = await db.SubscriptionRefunds
            .AnyAsync(r => r.SubscriptionInvoicePaymentId == invoice.Id, ct);
        if (alreadyRefunded) return;

        SubscriptionRefund refund = new()
        {
            SubscriptionId = subscription.Id,
            StudioId = subscription.StudioId,
            SubscriptionInvoicePaymentId = invoice.Id,
            StripeInvoiceId = invoice.StripeInvoiceId,
            Amount = quote.RefundAmount,
            Currency = invoice.Currency,
            MonthsUsed = quote.MonthsUsed,
            AmountPaid = quote.AmountPaid,
            MonthlyReferencePrice = quote.MonthlyReferencePrice,
            Rule = rule,
            InitiatedByUserId = initiatedByUserId,
            AdminReason = adminReason,
        };

        if (quote.RefundAmount > 0 && invoice.StripePaymentIntentId is not null)
        {
            // A3 — idempotency key = subscription id + current period start.
            string idempotencyKey = $"{subscription.StripeSubscriptionId}:{invoice.PeriodStart:O}";
            try
            {
                (string refundId, string status) = await billing.RefundAsync(
                    invoice.StripePaymentIntentId, (long)Math.Round(quote.RefundAmount * 100m), idempotencyKey, ct);
                refund.StripeRefundId = refundId;
                refund.Status = status == "succeeded" ? RefundStatus.Succeeded : RefundStatus.Pending;
            }
            catch (Exception ex)
            {
                refund.Status = RefundStatus.Failed;
                refund.FailureReason = ex.Message;
                logger.LogError(ex,
                    "Stripe refund failed for subscription {@SubscriptionId} invoice {@StripeInvoiceId}",
                    subscription.Id, invoice.StripeInvoiceId);
            }
        }
        else
        {
            // Zero-amount (AdminNone, or the formula computed 0) — nothing to call Stripe for.
            refund.Status = RefundStatus.Succeeded;
        }

        db.SubscriptionRefunds.Add(refund);
    }
}
```

A failed Stripe call must not throw out of the cancel handler (rule pattern §2.4/§2.5
already establish: cancellation stands regardless of Stripe's outcome, same as
`CancelSubscriptionHandler`'s existing best-effort `CancelSubscriptionAsync` try/catch,
`CancelSubscriptionCommand.cs:65-78`) — A6: "A `Failed` refund must alert the admin (error
log + visible on the studio's admin page); the studio stays cancelled." The error log above
satisfies the log half; §7 covers "visible on the studio's admin page".

### 2.7 Confirmation email

Owner-facing, not client-facing — follow `PastDueReminderJob.cs`'s inline-HTML pattern
(`Pena_e_Arte.Infrastructure/Jobs/PastDueReminderJob.cs:91-110`, `studio.OwnerEmail`,
`NotificationLog` with `RecipientType = NotificationRecipientType.Studio`), not
`IEmailRenderer`'s templated `Render*` methods (those are for client-facing emails like
`RenderPaymentRefunded` — a different domain). New
`SendSubscriptionCancelledNotificationCommand(Guid SubscriptionId, decimal RefundAmount,
int? MonthsUsed, SubscriptionStatus NewStatus, DateTime AccessEndDate)` in
`Pena_e_Arte.Application/Billing/Commands/`, dispatched from §2.4/§2.5's handlers via
`ISender`. Body per A4: refund amount (if > 0), "allow 5–10 business days" (only when a
refund was actually issued), the access-end date, and a link to `/billing/subscribe` to
resubscribe.

---

## 3. Flag, don't decide

- **Cash-billed cancellation timing.** A3 only says "Cash-billed studio: Monthly only; no
  refund path" — it doesn't specify whether access ends immediately or at
  `CurrentPeriodEnd`. §2.4's design cancels cash-billed subscriptions immediately (no Stripe
  subscription exists to schedule a period-end cancellation against, and there is no
  background job that would later flip a "pending cash cancellation" to `Cancelled`). Note
  this in your report as a deliberate simplification — zero real impact today (no paid
  studios), fast-follow if Finance wants cash-billed studios to keep access to the period
  they already paid for.
- **`StripePaymentIntentId` retrieval.** Confirm whether the installed Stripe.net 52.4.1
  SDK's `Invoice` type still exposes `PaymentIntentId` directly, or whether the ~2025-03
  invoice-payments API redesign (already visible in this codebase's
  `invoice.Parent?.SubscriptionDetails?.SubscriptionId` read, `BillingEndpoints.cs:194`)
  moved it under `invoice.Payments?.Data?.FirstOrDefault()?.Payment?.PaymentIntentId` or
  similar. Verify against a real Stripe test-mode `invoice.paid` payload during
  implementation (same practice Batch 2b used for `sub.Discounts`, which turned out to be
  `sub.Discounts?.Select(d => d.Source?.Coupon)` — a `Source` wrapper the original Batch 2b
  spec draft hadn't anticipated either) and note the actual shape used in your report.
- **`PeriodStart` source on the invoice webhook.** Likely `invoice.Lines?.Data?.FirstOrDefault()?.Period?.Start`
  — confirm against the same real payload above; a thin/restructured invoice's line-item
  shape may differ from older Stripe.net documentation.
- **Trialing/GracePeriod admin cancellations never have a paid yearly invoice** (no
  `SubscriptionInvoicePayment` row exists yet), so `Override` is a no-op for them — the
  refund branch's `invoice is not null` guard already handles this correctly (no invoice →
  no `SubscriptionRefund` row at all, not even a zero one). Confirm this reads correctly to
  an admin using the override toggle on such a studio (i.e., the UI shouldn't silently
  offer "Full refund" when there is nothing to refund) — a fast-follow if the admin UI needs
  a disabled state for this case; not worth blocking the batch on.

---

## 4. Scope boundary — do not touch

- Part B of the spec (`SubscriptionRevenueEvent` ledger, MRR movements chart, GRR/NRR
  tiles) — separate batch, `overnight-prompt-revenue-ledger-2026-09-24.md`. Do not add a
  `Churn` ledger event here; this batch only fixes `MrrRules`' existing D3 window via
  `CurrentPeriodEnd`/`CancelledAt` (§2.1.C), which is a bugfix to code Part B will later
  replace, not an implementation of Part B.
- `MrrRules.MonthlyEquivalent`, the billed-amount snapshot columns, `Discounts this month` —
  Batch 2b's territory, unchanged here except the one rename in §2.1.C.
- A8's launch checklist items 1, 2, 4 (creating live Stripe yearly prices, linking them in
  Plan management, publishing lawyer-reviewed terms) — those are Phi's/an admin's manual
  steps after this ships, not something to automate.
- A9 (Help/manual copy about the refund rule) — **intentionally not done in this batch**,
  see §11.

---

## 5. Phase A — Schema

Implement §2.1 in full: the three `SubscriptionInvoicePayment` columns, the new
`SubscriptionRefund` entity + configuration + two enums, the `IAppDbContext`/
`AppDbContext`/`FakeDbContext` `DbSet` additions, the `AuditActions.SubscriptionCancelledByOwner`
constant, and the `MrrInputLoader`/`MrrRules` `AdminCancelledAt` → `CancelledAt` rename. One
migration, `AddYearlyCancellationRefunds`.

**Tests:** none needed beyond the migration applying cleanly against a real MySQL instance
(`dotnet ef database update`) — schema-only phase. Run the full existing `MrrRulesTests.cs`
and `GetPlatformStatsHandlerTests.cs`/`GetMrrHistoryHandlerTests.cs` suites after the rename
to confirm nothing referenced the old field name outside `MrrInputLoader.cs`/`MrrRules.cs`.

Commit: `feat: add SubscriptionRefund table, invoice period/reference-price snapshot, owner-cancel audit action (Phase A)`

---

## 6. Phase B — Stripe service, refund calculator, refund issuer

Implement §2.2 (`IStripeBillingService` + `StripeBillingService` — three new methods, DI
registration for `RefundService`), §2.3 (`YearlyRefundCalculator`/`YearlyRefundQuote`), and
§2.6 (`YearlyRefundIssuer`).

**Tests:**

- `YearlyRefundCalculatorTests.cs` (new, `tests/Pena_e_Arte.UnitTests/Platform/Revenue/` next
  to `MrrRulesTests.cs`) — reproduce every cell of A2's table plus A10's acceptance rows:

| Scenario | Expect |
|---|---|
| Premium (79/mo), bought 1 Jan, cancelled 11 Mar | MonthsUsed 3; refund 553 |
| Same, cancelled 1 Jan same day | MonthsUsed 1; refund 711 |
| Bought 31 Jan, cancelled 29 Feb (leap year) | MonthsUsed 2 |
| Premium, cancelled in month 11 | Refund 0 |
| Growth referred (paid 531, monthly 59), month 3 | Refund 354 |
| `QuoteFor` with `PeriodStart` null (pre-snapshot invoice) | Returns null |
| `QuoteFor` with `latestInvoice` null | Returns null |

- `StripeBillingServiceTests.cs` (if a Stripe-service unit-test file exists for the other
  methods — otherwise these three are only integration-testable against real Stripe test
  mode; note that in your report rather than fabricating a no-assertion unit test, matching
  Batch 2b's stated practice for `StripeDemoSeeder`).

Commit: `feat: refund calculator, refund issuer, and Stripe schedule-cancellation/refund methods (Phase B)`

---

## 7. Phase C — Owner cancel flow (backend + frontend)

Implement §2.4 in full: `GetCancellationQuoteQuery`/`Handler`, `CancelMySubscriptionCommand`/
`Handler`, `KeepMySubscriptionCommand`/`Handler`, the three `BillingEndpoints.cs` routes,
and §2.7's confirmation-email command.

### Frontend — `BillingPage.tsx` + `billingApi.ts`

`billingApi.ts` additions (mirror the existing mutation/query shapes,
`frontend/src/features/billing/billingApi.ts:47-107`):

```typescript
getCancellationQuote: builder.query<CancellationQuoteResponse, void>({
  query: () => "billing/subscription/cancel/quote",
}),
cancelSubscription: builder.mutation<SubscriptionResponse, void>({
  query: () => ({ url: "billing/subscription/cancel", method: "POST" }),
  invalidatesTags: ["Subscription"],
}),
keepSubscription: builder.mutation<SubscriptionResponse, void>({
  query: () => ({ url: "billing/subscription/cancel", method: "DELETE" }),
  invalidatesTags: ["Subscription"],
}),
```

Add matching `CancellationQuoteResponse` type to `billing.types.ts` (camelCase mirror of the
C# contract — `billingInterval`, `refundAmount`, `monthsUsed`, `accessEndDate`,
`amountPaid`, `monthlyReferencePrice`).

`BillingPage.tsx` changes:

1. In the Active-card-billed actions row (`BillingPage.tsx:368-393`, next to "Change plan"/
   "Manage billing"), add a "Cancel plan" button (`variant="ghost"`, red text — match the
   destructive-action styling `alert-dialog.tsx`-based flows elsewhere in this codebase use,
   e.g. `EraseClientDataSection.tsx`) that opens an `AlertDialog`
   (`@/shared/components/ui/alert-dialog`). On open, fetch
   `useGetCancellationQuoteQuery(undefined, { skip: !dialogOpen })`. Dialog body:
   - Yearly with `refundAmount > 0`: "You'll get {formatEur(refundAmount)} back and lose
     access today." Show months used and the amount-paid breakdown below in smaller text.
   - Yearly with `refundAmount === 0`: "You won't get a refund (you've used the full value
     of this year's plan) and you'll lose access today."
   - Monthly: "You'll keep access until {formatDate(accessEndDate)}. No refund — you've
     already paid for this period."
   - Confirm button calls `cancelSubscription`, `toast.success`/`toast.error`, closes dialog.
2. In the "Subscription ending" card (`BillingPage.tsx:399-427`, currently shown for
   `sub.cancelAtPeriodEnd` and offering only "Manage billing"), replace that button with a
   "Keep my plan" button calling `useKeepSubscriptionMutation`, matching the existing
   `handleCancelPlanChange`/`toast` pattern used for the sibling "Scheduled plan change"
   card just below it (`BillingPage.tsx:447-457`).
3. `isCashBilled` studios: still show "Cancel plan" (no quote needed — always immediate, no
   refund) but skip the quote fetch; dialog body: "You'll lose access today. This
   subscription is billed in cash — no refund applies."

Run `pnpm test` and `pnpm lint` after.

**Tests:**

- `GetCancellationQuoteHandlerTests.cs`, `CancelMySubscriptionHandlerTests.cs`,
  `KeepMySubscriptionHandlerTests.cs` (new, `tests/Pena_e_Arte.UnitTests/Billing/`) — follow
  `CancelSubscriptionHandlerTests.cs`'s `FakeDbContext`/`Substitute.For<IStripeBillingService>()`
  seeding pattern (`tests/Pena_e_Arte.UnitTests/Platform/CancelSubscriptionHandlerTests.cs:14-21`).
  Cover: yearly with a snapshotted invoice → correct refund + Stripe calls; yearly with no
  invoice → 0 refund, still cancels; monthly → `ScheduleCancellationAsync` called, status
  stays Active, `CancelAtPeriodEnd=true`; cash-billed → immediate cancel, no Stripe calls;
  pending plan change → `CancelScheduledPriceChangeAsync` called first; already-cancelled
  status → throws; double-cancel (two calls against the same invoice) → only one
  `SubscriptionRefund` row, `RefundAsync` called once.
- A frontend test for the new dialog is optional given time — note in your report whether
  one was added; a real-browser pass (per this project's "Manual Browser Verification"
  standing practice for billing/tenant-affecting UI) is expected regardless.

Commit: `feat: owner-facing yearly-refund cancel flow, replaces portal cancellation (Phase C)`

---

## 8. Phase D — Admin override flow

Implement §2.5 in full and update the three existing admin cancel-button call sites
(`AdminStudioDetailPage.tsx:184-193`, `AdminStudioListPage.tsx`,
`SubscriptionOversightPage.tsx` — all currently call `cancelSub(studioId)` against
`platformApi.ts:110-113`'s `builder.mutation<void, string>`).

`platformApi.ts`: change the mutation to `builder.mutation<void, { studioId: string;
override?: "AdminFull" | "AdminNone"; reason?: string }>`, body `{ override, reason }` (send
neither field when not overriding). Update `AdminStudioListPage.tsx`'s and
`SubscriptionOversightPage.tsx`'s call sites to `cancelSub({ studioId })` (no override UI on
these list-row confirmations — keep them as one-click formula-default cancels, matching
their existing compact `AlertDialog` confirm pattern, `SubscriptionOversightPage.test.tsx:231-291`).

`AdminStudioDetailPage.tsx` (the one detail-page surface) gets the full override UI: inside
its existing cancel `AlertDialog` (around `handleCancel`, `AdminStudioDetailPage.tsx:184-193`),
add a radio group — "Use the formula (default)" / "Full refund" / "No refund" — and a reason
`Textarea` that appears and becomes required only when a non-default option is selected
(disable the Confirm button until a reason is entered). Follow whichever existing
reason-required destructive-admin-action pattern this codebase already has (check
`PlanEditPage.tsx`'s `AlertDialog` usage for a precedent worth matching) rather than
inventing new form-validation conventions.

**Tests:** extend `CancelSubscriptionHandlerTests.cs` with: `Override=null` uses the
formula; `AdminFull` refunds the full `AmountPaid` regardless of months used; `AdminNone`
records a zero-amount `Succeeded` `SubscriptionRefund` row and calls no Stripe refund;
missing `OverrideReason` when `Override` is set throws from the validator (add a
`CancelSubscriptionValidatorTests.cs` case if that test file doesn't already exist, or
extend it if it does).

Commit: `feat: admin refund override (full/none) with required reason (Phase D)`

---

## 9. Phase E — Refund status webhook, admin visibility, portal setting

### 9.1 `refund.updated` webhook

`BillingEndpoints.cs`'s `HandleBillingWebhook` switch (`BillingEndpoints.cs:185-233`) gets a
new case:

```csharp
case "refund.updated" or "charge.refund.updated" when stripeEvent.Data.Object is Refund refund:
    await mediator.Send(new HandleRefundUpdatedCommand(refund.Id, refund.Status, refund.FailureReason), ct);
    break;
```

Confirm the exact event-type string(s) Stripe fires for a refund status transition against
this account's configured webhook events (Stripe has historically used both
`charge.refund.updated` and, on newer API versions, `refund.updated` — check
`docs/claude/architecture.md`'s Stripe webhook-events list, if one exists, and the Stripe
Dashboard's configured webhook endpoint for this account's actual subscribed events; add
whichever is missing).

New `HandleRefundUpdatedCommand(string StripeRefundId, string StripeStatus, string?
FailureReason) : IRequest` + handler in `Pena_e_Arte.Application/Billing/Commands/`: find the
`SubscriptionRefund` by `StripeRefundId`, map status (`"succeeded"` → `Succeeded`,
`"failed"`/`"canceled"` → `Failed`, else leave `Pending`), set `UpdatedAt`, and on a
transition into `Failed`, `logger.LogError` with the studio/subscription id (A6's "alert the
admin" — log half) and set `FailureReason`.

### 9.2 Admin visibility for a failed refund

A6: "visible on the studio's admin page." Find `GetAdminStudioSummaryQuery`/
`AdminStudioSummaryResponse` (`Pena_e_Arte.Application/Support/Queries/` or similar — same
query `AdminStudioDetailPage.tsx` already reads from) and add a `RecentRefunds` list (last
5, most recent first: amount, status, createdAt, failureReason) sourced from
`db.SubscriptionRefunds.Where(r => r.StudioId == studioId)`. Render a small list/badge on
`AdminStudioDetailPage.tsx` — red badge for any `Failed` row.

### 9.3 A7 — "Refunds this month"

`GetPlatformStatsHandler.cs` gains, alongside the existing `discountsThisMonth` query
(`GetPlatformStatsQuery.cs:82-84`):

```csharp
decimal refundsThisMonth = await db.SubscriptionRefunds
    .Where(r => r.Status == RefundStatus.Succeeded && r.CreatedAt >= monthStart)
    .SumAsync(r => r.Amount, ct);
```

Add `decimal RefundsThisMonth` to `PlatformStatsResponse` (after `DiscountsThisMonth`) and
`platform.types.ts`. Do **not** add a new KPI tile for it — Batch 2b already noted the
three-per-row grid pattern is full; add it to `mrrSubtitle`'s helper the same way
`discountsThisMonth` was added (`AdminDashboardPage.tsx:199-213`) as a fourth clause, or —
preferred, since refunds are a cash outflow, not an MRR fact — as a small line under the
existing "At-Risk Studios" card, or its own minimal one-line card. Use your judgement,
matching the dashboard's existing information density; note your choice in the report.

### 9.4 Portal setting

Confirm the Stripe Dashboard test-mode customer-portal "Customers can cancel subscriptions"
toggle is off (§2.4) — this is a manual dashboard action, not code; do it now and state in
your report that it was done, plus a reminder that A8 item 3 repeats this for live mode at
launch.

**Tests:** `HandleRefundUpdatedHandlerTests.cs` — succeeded/failed/duplicate-event
(idempotent — same `StripeRefundId` twice, no double-processing needed since it's a status
update not an insert) cases. `GetPlatformStatsHandlerTests.cs` — `RefundsThisMonth` sums
only `Succeeded` rows created this calendar month.

Commit: `feat: refund status webhook, admin refund visibility, refunds-this-month stat (Phase E)`

---

## 10. Phase F — Architecture docs (Help/manual deliberately NOT touched — see §11)

`architecture.md`: new Decisions Log entry, "Yearly cancellation refunds (2026-09-24)" —
summarize §2.1–§2.7, the `AdminCancelledAt`→`CancelledAt` rename and why (§2.1.C), and the
new `SubscriptionRefund`/extended `SubscriptionInvoicePayment` shapes so Batch 3b's ledger
work can reference them without re-deriving this design. Confirm no new
`IgnoreQueryFilters()` registry row is needed (§2.1.B) and say so explicitly.

Commit: `docs: yearly cancellation refunds decisions log entry (Phase F)`

---

## 11. CLAUDE.md rule #7 exception — explicit, not silent

CLAUDE.md rule #7 requires Help Menu, manual, and onboarding-tour updates "in the same
change" as any user-facing feature. **This batch deliberately does not update
`helpContent.ts` or the standalone manual for the refund rule.** Reason, straight from the
spec (A8/A9): Starter/Growth yearly aren't purchasable yet, the refund terms need a lawyer's
review before publication, and Batches 1–2 already established the precedent of leaving
launch-gated copy out of Help until the gate clears ("Batches 1–2 deliberately left it out").
Publishing a Help article promising a specific refund formula before it's legally reviewed
and the plans are actually live would be actively wrong, not merely incomplete. A9 explicitly
schedules this for *after* A8's launch checklist. This is a deliberate, spec-directed
exception to rule #7, not an oversight — flag it as such in your final report, and leave a
one-line TODO comment at the top of `helpContent.ts`'s existing "Choose or change your plan"
owner article referencing A9 and this file, so the next batch that touches Help finds it.

The in-app UI copy the feature needs to function (the cancellation quote dialog's "You'll
get €X back" text, the confirmation email body) is **not** gated by this — it ships normally
in Phase C/E, since without it the feature doesn't work at all.

---

## 12. Industry-standard benchmark note (CLAUDE.md rule #6)

A time-based/prorated refund on annual-plan cancellation (SaaS platforms typically refund
nothing, or only within a short cooling-off window) is unusually generous; charging the used
months at the monthly rate while keeping the two free months' value for a stayer is closer
to how usage-based SaaS mid-market vendors (Vagaro/Fresha-tier) structure annual-commitment
refunds when they offer one at all — most in this category offer *no* refund path for
annual plans, making even this formula a differentiator worth the UX investment in a clear
pre-confirmation quote (A4's "show a quote first" requirement matches Stripe's own
subscription-cancellation UX guidance of previewing financial consequences before a
destructive, irreversible action).

---

## 13. Final verification checklist

- [ ] A2's sixteen worked values reproduce exactly via `YearlyRefundCalculator`.
- [ ] A10's eleven acceptance scenarios all pass.
- [ ] Owner yearly cancel: Stripe subscription cancelled immediately, refund issued against
      the correct invoice's `PaymentIntent`, access blocked immediately
      (`TenantMiddleware.cs`'s Cancelled-status block, `TenantMiddleware.cs:133-134`), owner
      redirected to `/subscribe`.
- [ ] Owner monthly cancel: `cancel_at_period_end=true`, access continues to period end,
      "Keep my plan" reverses it.
- [ ] Admin override: full/none both work, reason required and stored in audit metadata,
      default (no override) matches the owner formula exactly.
- [ ] Double-cancel (network retry) produces exactly one Stripe refund and one
      `SubscriptionRefund` row.
- [ ] A failed Stripe refund: `SubscriptionRefund.Status=Failed`, error logged, visible on
      the admin studio page, subscription stays cancelled.
- [ ] `MrrRules`' D3 window now ends precisely at cancellation time for both owner- and
      admin-initiated yearly cancellations (§2.1.C) — spot-check against
      `GetMrrHistoryHandlerTests.cs`.
- [ ] Portal cancellation confirmed off in Stripe test mode.
- [ ] `helpContent.ts` untouched except the one TODO comment (§11) — verify the CI "Help
      stays in sync" gate (`docs/claude/architecture.md`'s CI Pipeline notes) doesn't fail on
      this deliberately-deferred copy; use `[skip-help-sync]` in the final commit message if
      the gate requires it, and say so in your report.
- [ ] `dotnet test`, `pnpm test`, `pnpm lint` all green.
- [ ] Six commits on `feature/yearly-cancellation-refunds` (Phases A–F).

---

## 14. Final deliverable spec

Six commits (Phases A–F). Final report: changes per phase with file list; drift from this
file; the §3 flags verbatim (cash-billed timing, `StripePaymentIntentId`/`PeriodStart`
retrieval verified against a real payload, Trialing/GracePeriod override no-op UX); the §11
rule-7 exception restated plainly; confirmation that Batch 3b (the revenue ledger) can add a
`Churn` event at the exact moment §2.4/§2.5 flip `Status` to `Cancelled` without needing any
further schema change here.
