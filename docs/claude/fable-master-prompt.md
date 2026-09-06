# Fable Overnight Master Prompt
## Full Codebase Review + Complete Implementation

> **Model:** Use Fable (claude-fable or equivalent extended-context model) for this run.
> **Estimated scope:** ~30–50 files to create or modify across backend and frontend.
> **Strategy:** Read → Audit → Fix → Implement → Test. Never skip a phase.

---

## STEP 0 — Load All Context (do this first, before any code changes)

Read every file listed below in full before touching anything:

```
CLAUDE.md
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/database.md
docs/claude/architecture.md
docs/claude/conventions.md
```

Then read these specific source files to understand current state:

```
Pena_e_Arte.Domain/Entities/Payment.cs
Pena_e_Arte.Domain/Entities/Studio.cs
Pena_e_Arte.Domain/Entities/Subscription.cs
Pena_e_Arte.Domain/Entities/Plan.cs
Pena_e_Arte.Domain/Enums/ClientPaymentMethod.cs
Pena_e_Arte.Domain/Enums/PaymentStatus.cs
Pena_e_Arte.Domain/Enums/DepositStatus.cs
Pena_e_Arte.Domain/Interfaces/IStripePaymentService.cs
Pena_e_Arte.Domain/Interfaces/IStripeConnectService.cs
Pena_e_Arte.Infrastructure/Services/StripePaymentService.cs
Pena_e_Arte.Infrastructure/Services/StripeConnectService.cs
Pena_e_Arte.Application/Studios/Commands/ConnectStudioCommand.cs
Pena_e_Arte.Application/Payments/Commands/ConfirmPaymentCommand.cs
Pena_e_Arte.API/Endpoints/PaymentEndpoints.cs
Pena_e_Arte.API/Endpoints/PlatformEndpoints.cs
Pena_e_Arte.API/Endpoints/BillingEndpoints.cs
Pena_e_Arte.API/Endpoints/ReferralEndpoints.cs
Pena_e_Arte.API/Program.cs
frontend/src/app/router.tsx
frontend/src/app/store.ts
frontend/src/features/payments/paymentsApi.ts
frontend/src/features/payments/payment.types.ts
frontend/src/features/payments/components/PaymentMethodSelector.tsx
frontend/src/features/payments/components/CashDepositConfirmButton.tsx
frontend/src/features/payments/components/DepositCheckoutPage.tsx
frontend/src/features/billing/billingApi.ts
frontend/src/features/billing/billing.types.ts
frontend/src/features/billing/components/BillingPage.tsx
frontend/src/features/billing/components/SubscribePage.tsx
frontend/src/features/platform/platformApi.ts
frontend/src/features/platform/platform.types.ts
frontend/src/features/platform/components/AdminDashboardPage.tsx
frontend/src/features/platform/components/SubscriptionOversightPage.tsx
frontend/src/features/platform/components/PlanManagementPage.tsx
frontend/src/features/platform/components/PlatformReferralPage.tsx
frontend/src/features/studios/components/ConnectStudioPage.tsx
frontend/src/features/studios/studiosApi.ts
frontend/src/features/dashboard/components/DashboardPage.tsx
frontend/src/main.tsx
```

Also run:
```bash
dotnet build 2>&1 | head -60
cd frontend && pnpm tsc --noEmit 2>&1 | head -60
```

Record all build errors before starting. Fix them as you encounter the relevant section.

---

## STEP 1 — Remove Stripe Connect Entirely

Stripe Connect is not available in the platform's country. All Connect-related code must
be removed or replaced. This is a prerequisite for all payment work.

### 1.1 Mark `StripeConnectService` obsolete and strip active usage

**Edit** `Pena_e_Arte.Infrastructure/Services/StripeConnectService.cs`:
Add `[Obsolete("Stripe Connect is not available in this region. Do not call.")]`
to the class declaration. Do NOT delete the file yet — wait until the DI registration
and command are removed first, or the build will fail.

### 1.2 Replace `ConnectStudioCommand`

The existing `ConnectStudioCommand` calls `IStripeConnectService.ConnectStudioAsync`.
This flow is permanently dead.

**Delete:**
```
Pena_e_Arte.Application/Studios/Commands/ConnectStudioCommand.cs
Pena_e_Arte.Application/Studios/Validators/ConnectStudioValidator.cs
```

### 1.3 Remove Connect from StudioEndpoints

**Edit** `Pena_e_Arte.API/Endpoints/StudioEndpoints.cs`:
Remove any `ConnectStudio` endpoint registration and its private handler method.
Remove `POST /api/v1/studios/{id}/connect` route entirely.

### 1.4 Remove Connect DI registration

**Edit** the infrastructure DI registration file (likely
`Pena_e_Arte.Infrastructure/Extensions/` or `Program.cs`):
Remove `services.AddScoped<IStripeConnectService, StripeConnectService>()`.

### 1.5 Delete Connect interface and service

After removing all usages, delete:
```
Pena_e_Arte.Domain/Interfaces/IStripeConnectService.cs
Pena_e_Arte.Domain/Exceptions/StripeAccountNotConnectedException.cs
Pena_e_Arte.Infrastructure/Services/StripeConnectService.cs
```

### 1.6 Remove Connect fields from `Studio` entity

**Edit** `Pena_e_Arte.Domain/Entities/Studio.cs`:
Remove `StripeAccountId` property if present. Keep `StripeCustomerId`.
Update `AppDbContext` / EF configuration to remove the column if mapped.

### 1.7 Fix `StripePaymentService` — remove all connected account calls

**Read** `Pena_e_Arte.Infrastructure/Services/StripePaymentService.cs` carefully.
Find every call that passes `RequestOptions { StripeAccount = ... }`.
Remove the parameter completely — no connected account, platform account only.

The correct `CreatePaymentIntentAsync` must have NO `StripeAccount` in the options.
See `docs/claude/payment-simplified-prompt.md` Section 3.1 for the exact implementation.

### 1.8 Update `IStripePaymentService` interface

**Read** `Pena_e_Arte.Domain/Interfaces/IStripePaymentService.cs`.
Remove the `connectedAccountId` parameter from every method signature if present.
The interface must match Section 3.2 of `docs/claude/payment-simplified-prompt.md`.

### 1.9 Frontend — remove Connect pages and routes

**Delete:**
```
frontend/src/features/studios/components/ConnectStudioPage.tsx
frontend/src/features/studios/components/ConnectReturnPage.tsx
frontend/src/features/studios/components/ConnectRefreshPage.tsx
```

**Edit** `frontend/src/app/router.tsx`:
Remove routes for `/connect`, `/connect/return`, `/connect/refresh`.
Remove any `connectStudio` RTK Query endpoints from `studiosApi.ts` if present.

### 1.10 Generate a migration to drop `StripeAccountId` column

```bash
dotnet ef migrations add RemoveStripeConnect \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
dotnet ef database update \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
```

Verify the migration drops `StripeAccountId` from the `Studios` table.

---

## STEP 2 — Fix the Payment Domain

### 2.1 Fix `ClientPaymentMethod` enum

**Read** `Pena_e_Arte.Domain/Enums/ClientPaymentMethod.cs`.

If the values are NOT exactly `Card` and `Cash`, replace the file:

```csharp
namespace Pena_e_Arte.Domain.Enums;

public enum ClientPaymentMethod
{
    Card,
    Cash,
}
```

If a migration is needed to rename enum values stored as strings in the DB, create one.

### 2.2 Fix `PaymentStatus` enum

**Read** `Pena_e_Arte.Domain/Enums/PaymentStatus.cs`.

Ensure `CashPending` is present. Replace the file if not:

```csharp
namespace Pena_e_Arte.Domain.Enums;

public enum PaymentStatus
{
    /// <summary>Card payment intent created, awaiting client action.</summary>
    Pending,

    /// <summary>Client selected cash; awaiting owner/artist confirmation of receipt.</summary>
    CashPending,

    /// <summary>Card deposit authorised (held), not yet captured.</summary>
    Captured,

    /// <summary>Fully received — card captured or cash confirmed.</summary>
    Paid,

    /// <summary>Refunded.</summary>
    Refunded,

    /// <summary>Card payment failed.</summary>
    Failed,
}
```

### 2.3 Fix `Payment` entity

**Read** `Pena_e_Arte.Domain/Entities/Payment.cs`.

Ensure it has exactly these fields (add any that are missing):

```csharp
public class Payment : TenantEntity
{
    public Guid               AppointmentId         { get; set; }
    public Guid               ClientId              { get; set; }
    public decimal            Amount                { get; set; }
    public PaymentStatus      Status                { get; set; } = PaymentStatus.Pending;
    public ClientPaymentMethod Method               { get; set; } = ClientPaymentMethod.Card;

    // Card (Stripe) — null for cash
    public string? StripePaymentIntentId            { get; set; }
    public string? ClientSecret                     { get; set; }

    // Cash — null for card
    public string? CashNote                         { get; set; }
    public Guid?   CashConfirmedByUserId            { get; set; }

    public DateTime? PaidAt                         { get; set; }

    public Appointment                    Appointment   { get; set; } = null!;
    public Client                         Client        { get; set; } = null!;
    public ICollection<SessionSplit>      SessionSplits { get; set; } = [];
}
```

Remove `PayPalOrderId`, `PayPalCaptureId` if present.

### 2.4 Update EF Core Payment configuration

**Edit** `Pena_e_Arte.Infrastructure/Persistence/Configurations/PaymentConfiguration.cs`:
Ensure these are mapped:

```csharp
builder.Property(x => x.Method).HasConversion<string>().HasMaxLength(10);
builder.Property(x => x.CashNote).HasMaxLength(500);
builder.Property(x => x.CashConfirmedByUserId).IsRequired(false);
```

### 2.5 Generate payment migration

```bash
dotnet ef migrations add AddCashPaymentFields \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
dotnet ef database update \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
```

The migration should add: `Method` (string), `CashNote` (string nullable),
`CashConfirmedByUserId` (Guid nullable) to the `Payments` table.

### 2.6 Fix existing payment command handlers

**Read** every existing payment command handler:
- `ConfirmPaymentCommand` — ensure it does NOT reference `StripeAccountId` or `connectedAccountId`
- `CreatePaymentIntentCommand` (if it exists) — same check, fix if needed
- If `CreatePaymentIntentCommand` doesn't exist, check `PaymentEndpoints.cs` for the card payment intent creation logic and extract it to a proper command handler

Ensure card payment handlers set `Method = ClientPaymentMethod.Card`.

---

## STEP 3 — Implement Cash Payment Commands

Follow `docs/claude/payment-simplified-prompt.md` Sections 4.2, 4.3.

### 3.1 Create `DeclareCashDepositCommand`

**Create** `Pena_e_Arte.Application/Payments/Commands/DeclareCashDepositCommand.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Application.Payments.Commands;

public record DeclareCashDepositCommand(Guid AppointmentId, string? Note)
    : IRequest<PaymentResponse>;

public class DeclareCashDepositHandler(AppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<DeclareCashDepositCommand, PaymentResponse>
{
    public async Task<PaymentResponse> Handle(
        DeclareCashDepositCommand command, CancellationToken ct)
    {
        Appointment appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct)
            ?? throw new NotFoundException(nameof(Appointment), command.AppointmentId);

        bool alreadyExists = await db.Payments
            .AnyAsync(p => p.AppointmentId == command.AppointmentId, ct);
        if (alreadyExists)
            throw new BusinessRuleViolationException(
                "A payment record already exists for this appointment.");

        Payment payment = new()
        {
            AppointmentId = appointment.Id,
            ClientId      = appointment.ClientId,
            Amount        = appointment.DepositAmount,
            Method        = ClientPaymentMethod.Cash,
            Status        = PaymentStatus.CashPending,
            CashNote      = command.Note,
            TenantId      = tenant.StudioId,
        };

        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);
        return payment.ToPaymentResponse();
    }
}
```

**Create** `Pena_e_Arte.Application/Payments/Validators/DeclareCashDepositValidator.cs`:

```csharp
using FluentValidation;

namespace Pena_e_Arte.Application.Payments.Validators;

public class DeclareCashDepositValidator : AbstractValidator<DeclareCashDepositCommand>
{
    public DeclareCashDepositValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note is not null);
    }
}
```

### 3.2 Create `ConfirmCashDepositCommand`

**Create** `Pena_e_Arte.Application/Payments/Commands/ConfirmCashDepositCommand.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Application.Payments.Commands;

public record ConfirmCashDepositCommand(Guid PaymentId) : IRequest<PaymentResponse>;

public class ConfirmCashDepositHandler(AppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ConfirmCashDepositCommand, PaymentResponse>
{
    public async Task<PaymentResponse> Handle(
        ConfirmCashDepositCommand command, CancellationToken ct)
    {
        Payment payment = await db.Payments
            .Include(p => p.Appointment)
            .FirstOrDefaultAsync(p => p.Id == command.PaymentId, ct)
            ?? throw new NotFoundException(nameof(Payment), command.PaymentId);

        if (payment.Method != ClientPaymentMethod.Cash)
            throw new BusinessRuleViolationException("This payment is not a cash payment.");

        if (payment.Status != PaymentStatus.CashPending)
            throw new BusinessRuleViolationException(
                "This cash payment has already been processed.");

        payment.Status                = PaymentStatus.Paid;
        payment.PaidAt                = DateTime.UtcNow;
        payment.CashConfirmedByUserId = currentUser.UserId;

        if (payment.Appointment is not null)
            payment.Appointment.DepositStatus = DepositStatus.Paid;

        await db.SaveChangesAsync(ct);
        return payment.ToPaymentResponse();
    }
}
```

**Create** `Pena_e_Arte.Application/Payments/Validators/ConfirmCashDepositValidator.cs`:

```csharp
public class ConfirmCashDepositValidator : AbstractValidator<ConfirmCashDepositCommand>
{
    public ConfirmCashDepositValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
    }
}
```

### 3.3 Add cash endpoints to `PaymentEndpoints.cs`

**Edit** `Pena_e_Arte.API/Endpoints/PaymentEndpoints.cs`.
Read the file first to understand the existing group structure.
Add below the existing endpoints:

```csharp
group.MapPost("/cash",
    DeclareCashDeposit).RequireAuthorization("ClientAndAbove");

group.MapPost("/{id:guid}/cash/confirm",
    ConfirmCashDeposit).RequireAuthorization("ArtistAndAbove");

private static async Task<IResult> DeclareCashDeposit(
    DeclareCashDepositRequest request, ISender mediator, CancellationToken ct)
{
    PaymentResponse result = await mediator.Send(
        new DeclareCashDepositCommand(request.AppointmentId, request.Note), ct);
    return Results.Created($"/api/v1/payments/{result.Id}", result);
}

private static async Task<IResult> ConfirmCashDeposit(
    Guid id, ISender mediator, CancellationToken ct)
{
    PaymentResponse result = await mediator.Send(
        new ConfirmCashDepositCommand(id), ct);
    return Results.Ok(result);
}
```

### 3.4 Add Contracts

**Create** `Pena_e_Arte.Contracts/Requests/DeclareCashDepositRequest.cs`:
```csharp
namespace Pena_e_Arte.Contracts.Requests;
public record DeclareCashDepositRequest(Guid AppointmentId, string? Note);
```

**Update** `Pena_e_Arte.Contracts/Responses/PaymentResponse.cs` — ensure `Method` and cash
fields are present:

```csharp
namespace Pena_e_Arte.Contracts.Responses;

public record PaymentResponse(
    Guid      Id,
    Guid      AppointmentId,
    decimal   Amount,
    string    Status,
    string    Method,
    string?   StripePaymentIntentId,
    string?   ClientSecret,
    string?   CashNote,
    DateTime? PaidAt);
```

Add or update `ToPaymentResponse()` extension method on `Payment` entity accordingly.

---

## STEP 4 — Implement Cash Subscription Activation

### 4.1 Create `ActivateSubscriptionManuallyCommand`

**Create** `Pena_e_Arte.Application/Billing/Commands/ActivateSubscriptionManuallyCommand.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Infrastructure.Persistence;
using Serilog;

namespace Pena_e_Arte.Application.Billing.Commands;

public record ActivateSubscriptionManuallyCommand(
    Guid    StudioId,
    Guid    PlanId,
    string? Note)
    : IRequest<SubscriptionResponse>;

public class ActivateSubscriptionManuallyHandler(AppDbContext db)
    : IRequestHandler<ActivateSubscriptionManuallyCommand, SubscriptionResponse>
{
    public async Task<SubscriptionResponse> Handle(
        ActivateSubscriptionManuallyCommand command, CancellationToken ct)
    {
        Studio studio = await db.Studios
            .IgnoreQueryFilters()
            .Include(s => s.Subscription)
            .FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new NotFoundException(nameof(Studio), command.StudioId);

        Plan plan = await db.Plans
            .FirstOrDefaultAsync(p => p.Id == command.PlanId, ct)
            ?? throw new NotFoundException(nameof(Plan), command.PlanId);

        if (studio.Subscription is null)
        {
            studio.Subscription = new Subscription
            {
                StudioId         = studio.Id,
                PlanId           = plan.Id,
                Status           = SubscriptionStatus.Active,
                CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1),
            };
            db.Subscriptions.Add(studio.Subscription);
        }
        else
        {
            studio.Subscription.PlanId          = plan.Id;
            studio.Subscription.Status          = SubscriptionStatus.Active;
            studio.Subscription.CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1);
        }

        Log.Information(
            "Subscription manually activated for studio {@StudioId} on plan {@PlanId}",
            studio.Id, plan.Id);

        await db.SaveChangesAsync(ct);
        return studio.Subscription.ToSubscriptionResponse();
    }
}
```

**Create** `Pena_e_Arte.Application/Billing/Validators/ActivateSubscriptionManuallyValidator.cs`:

```csharp
public class ActivateSubscriptionManuallyValidator
    : AbstractValidator<ActivateSubscriptionManuallyCommand>
{
    public ActivateSubscriptionManuallyValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
        RuleFor(x => x.PlanId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(500).When(x => x.Note is not null);
    }
}
```

**Create** `Pena_e_Arte.Contracts/Requests/ActivateSubscriptionManuallyRequest.cs`:
```csharp
public record ActivateSubscriptionManuallyRequest(Guid PlanId, string? Note);
```

### 4.2 Add endpoint to `PlatformEndpoints.cs`

**Edit** `Pena_e_Arte.API/Endpoints/PlatformEndpoints.cs`.
Read the full file first. Add:

```csharp
group.MapPost("studios/{studioId:guid}/subscription/activate",
    ActivateSubscriptionManually).RequireAuthorization("AdminOnly");

private static async Task<IResult> ActivateSubscriptionManually(
    Guid                                 studioId,
    ActivateSubscriptionManuallyRequest  request,
    ISender                              mediator,
    CancellationToken                    ct)
{
    SubscriptionResponse result = await mediator.Send(
        new ActivateSubscriptionManuallyCommand(studioId, request.PlanId, request.Note), ct);
    return Results.Ok(result);
}
```

---

## STEP 5 — Implement Admin Platform Features

These are fully new features that haven't been implemented yet.
Follow `docs/claude/admin-dashboard-prompt.md` exactly for F1–F6 and FIX.

### 5.1 FIX — Remove duplicate plan CRUD

**Check** if `Pena_e_Arte.API/Endpoints/AdminEndpoints.cs` exists.
If yes, delete it and remove `app.MapAdminEndpoints()` from `Program.cs`.
If it doesn't exist, skip.

### 5.2 F6 — AllowBrandingRemoval on UpdatePlan

Read `Pena_e_Arte.Application/Plans/Commands/` to find `UpdatePlanCommand`.
Read `Pena_e_Arte.Contracts/Requests/UpdatePlanRequest.cs`.

If `AllowBrandingRemoval` is missing from the request/command/handler, add it.
Follow Section F6 of `admin-dashboard-prompt.md` exactly.

### 5.3 F1 — Platform Statistics API

Implement in full:
- `GetPlatformStatsQuery` + `GetPlatformStatsHandler` (AdminOnly, `IgnoreQueryFilters()`)
- `PlatformStatsResponse` contract
- `GET /api/v1/platform/stats` endpoint

The query aggregates across all tenants (approved `IgnoreQueryFilters()` usage #4):
- `TotalStudios` — count of all non-suspended studios
- `ActiveSubscriptions` — count where `Subscription.Status == Active`
- `TrialStudios` — count where `Now < TrialExpiresAt`
- `GracePeriodStudios` — count where in grace period
- `Mrr` — sum of active subscription monthly prices
- `TrialConversionRate` — `ActiveSubscriptions / (ActiveSubscriptions + TrialStudios + GracePeriodStudios)`
- `NewStudiosThisMonth` — count created in current calendar month

Follow Section F1 of `admin-dashboard-prompt.md` for the full handler implementation.

### 5.4 F2 — Subscription Oversight

Implement:
- `GetPlatformSubscriptionsQuery` + handler (AdminOnly, `IgnoreQueryFilters()`)
- `ExtendTrialCommand` + handler (AdminOnly, max 90-day cap)
- `PlatformSubscriptionResponse` contract
- `GET /api/v1/platform/subscriptions` endpoint
- `PATCH /api/v1/platform/subscriptions/{studioId}/trial` endpoint

Follow Section F2 of `admin-dashboard-prompt.md` exactly.

### 5.5 F3 — Platform Referral Code Management

Implement:
- `GetPlatformReferralCodesQuery` + handler (AdminOnly, `IgnoreQueryFilters()`)
- `DeactivateReferralCodeCommand` + handler (AdminOnly)
- `PlatformReferralCodeResponse` contract
- `GET /api/v1/platform/referral-codes` endpoint
- `PATCH /api/v1/platform/referral-codes/{id}/deactivate` endpoint

Follow Section F3 of `admin-dashboard-prompt.md` exactly.

---

## STEP 6 — Frontend: Fix Payment UI

### 6.1 Fix `payment.types.ts`

**Read** `frontend/src/features/payments/payment.types.ts`.
Ensure it matches exactly:

```typescript
export type PaymentStatus =
  | "Pending"
  | "CashPending"
  | "Captured"
  | "Paid"
  | "Refunded"
  | "Failed";

export type PaymentMethod = "Card" | "Cash";

export interface PaymentResponse {
  id:                    string;
  appointmentId:         string;
  amount:                number;
  status:                PaymentStatus;
  method:                PaymentMethod;
  stripePaymentIntentId: string | null;
  clientSecret:          string | null;
  cashNote:              string | null;
  paidAt:                string | null;
}
```

Remove any `PayPal*` types. Remove `PayoutMethodResponse`, `StudioPayoutResponse`.

### 6.2 Fix `paymentsApi.ts`

**Read** `frontend/src/features/payments/paymentsApi.ts`.
Verify the following endpoints exist and are correctly typed:
- `getPayments` — list
- `getPayment(id)` — single
- `createPaymentIntent` — card path
- `declareCashDeposit` — mutation, `POST payments/cash`
- `confirmCashDeposit` — mutation, `POST payments/{id}/cash/confirm`

Remove any `createPayPalOrder`, `capturePayPalOrder`, payout endpoints.
Tag types must be `["Payment"]` only — no `PayoutMethod`, `Payout`.

### 6.3 Fix `PaymentMethodSelector.tsx`

**Read** `frontend/src/features/payments/components/PaymentMethodSelector.tsx`.
If it still references PayPal, has a PayPal tab, or imports `@paypal/react-paypal-js`:
Replace it entirely with the implementation from `docs/claude/payment-simplified-prompt.md`
Section 7.3.

The component must have exactly two tabs: **Card** (Stripe `PaymentElement`) and **Cash**
(informational panel + `declareCashDeposit` mutation).

### 6.4 Fix `CashDepositConfirmButton.tsx`

**Read** `frontend/src/features/payments/components/CashDepositConfirmButton.tsx`.
If it doesn't call `confirmCashDeposit` properly or has incorrect types,
replace it with the implementation from Section 7.4 of `payment-simplified-prompt.md`.

### 6.5 Fix `main.tsx` — remove PayPal provider

**Read** `frontend/src/main.tsx`.
If it has `PayPalScriptProvider` anywhere: remove it and the import.
Run `pnpm remove @paypal/react-paypal-js` if the package is still in `package.json`.

### 6.6 Update `DashboardPage.tsx` — cash pending section

**Read** `frontend/src/features/dashboard/components/DashboardPage.tsx`.
Add a `CashPendingSection` below the today's appointments section.
It queries payments and filters `status === "CashPending"`.
Renders each with `CashDepositConfirmButton`.
Section is hidden if no cash-pending payments.
Use `Banknote` icon from `lucide-react`, title "Awaiting Cash".

### 6.7 Fix `SubscribePage.tsx` — add cash info block

**Read** `frontend/src/features/billing/components/SubscribePage.tsx`.
At the bottom of the form (after the Stripe card section), add:

```tsx
<div className="mt-6 rounded-lg border border-input p-4 space-y-2 text-sm">
  <p className="font-medium flex items-center gap-2">
    <Banknote className="h-4 w-4" />
    Prefer to pay cash?
  </p>
  <p className="text-muted-foreground">
    Contact us and we will activate your subscription once payment is confirmed.
    Your trial continues until then.
  </p>
  <a
    href={`mailto:${import.meta.env.VITE_CONTACT_EMAIL}`}
    className="text-sm font-medium underline underline-offset-4"
  >
    Get in touch
  </a>
</div>
```

---

## STEP 7 — Frontend: Fix Admin Platform Features

### 7.1 Fix `platform.types.ts`

**Read** `frontend/src/features/platform/platform.types.ts`.
Ensure these interfaces exist (add missing ones):

```typescript
export interface PlatformStatsResponse {
  totalStudios:          number;
  activeSubscriptions:   number;
  trialStudios:          number;
  gracePeriodStudios:    number;
  mrr:                   number;
  trialConversionRate:   number;
  newStudiosThisMonth:   number;
}

export interface PlatformSubscriptionResponse {
  studioId:        string;
  studioName:      string;
  planName:        string | null;
  status:          string;
  currentPeriodEnd: string | null;
  trialExpiresAt:  string | null;
}

export interface PlatformReferralCodeResponse {
  id:          string;
  studioName:  string;
  code:        string;
  isActive:    boolean;
  createdAt:   string;
  expiresAt:   string | null;
  redemptions: number;
}
```

### 7.2 Fix `platformApi.ts`

**Read** `frontend/src/features/platform/platformApi.ts`.
Ensure these endpoints are present and correctly typed:

```typescript
getPlatformStats         // GET platform/stats → PlatformStatsResponse
getPlatformSubscriptions // GET platform/subscriptions → PlatformSubscriptionResponse[]
extendTrial              // PATCH platform/subscriptions/{studioId}/trial
getPlatformReferralCodes // GET platform/referral-codes → PlatformReferralCodeResponse[]
deactivateReferralCode   // PATCH platform/referral-codes/{id}/deactivate
getAdminPlans           // GET billing/plans → PlanResponse[] (for plan selector)
activateSubscriptionManually // POST platform/studios/{studioId}/subscription/activate
```

Tag types: `PlatformStats`, `PlatformSubscription`, `PlatformReferral`.

### 7.3 Fix `AdminDashboardPage.tsx`

**Read** `frontend/src/features/platform/components/AdminDashboardPage.tsx`.

This is the admin home screen. It must show:
1. **KPI stat cards** — using `useGetPlatformStatsQuery()`:
   - Total Studios (with `Building2` icon)
   - Active Subscriptions (with `CreditCard` icon)
   - MRR formatted as currency (with `TrendingUp` icon)
   - Trial Conversion Rate as percentage (with `Users` icon)
2. **At-Risk Widget** — from `useGetPlatformSubscriptionsQuery()`, filtered to
   `status === "GracePeriod"` or `status === "PastDue"`. Shows studio name + expiry date.
   If empty, show "No at-risk studios."
3. **Quick Nav tiles** linking to `/platform/studios`, `/platform/plans`,
   `/platform/subscriptions`, `/platform/referrals`, `/platform/reports`.

If the page is a stub or incomplete, rewrite it fully. Each stat card uses `Card` from
shadcn/ui. Wrap in loading skeleton (`Skeleton`) while query is fetching.

### 7.4 Fix `SubscriptionOversightPage.tsx`

**Read** `frontend/src/features/platform/components/SubscriptionOversightPage.tsx`.

Must display all studios' subscription status using `useGetPlatformSubscriptionsQuery()`.
For studios with `status` of `NoSubscription`, `GracePeriod`, or `Cancelled`:
- Show "Extend trial" button → calls `extendTrial` mutation
- Show "Activate (cash)" button → opens inline form with plan selector + note input
  → calls `activateSubscriptionManually` mutation

Plan selector is populated from `useGetAdminPlansQuery()`.

### 7.5 Fix `PlatformReferralPage.tsx`

**Read** `frontend/src/features/platform/components/PlatformReferralPage.tsx`.

Must display all referral codes using `useGetPlatformReferralCodesQuery()`.
Active codes have a "Deactivate" button → calls `deactivateReferralCode` mutation.
Shows: studio name, code, redemptions, created date, expiry, active badge.

### 7.6 Fix `PlanManagementPage.tsx`

**Read** `frontend/src/features/platform/components/PlanManagementPage.tsx`.

Ensure the form for creating/editing a plan includes `allowBrandingRemoval` toggle
(boolean, rendered as a `Switch` or checkbox).

### 7.7 Verify `store.ts` has `platformApi` and `paymentsApi`

**Read** `frontend/src/app/store.ts`.
Ensure `platformApi` and `paymentsApi` are registered (reducer + middleware).
Add them if missing.

### 7.8 Verify `router.tsx` has all admin routes

**Read** `frontend/src/app/router.tsx`.
Ensure all platform routes exist under `RoleGuard allowedRoles={["admin"]}`:
```
/platform             → AdminDashboardPage
/platform/studios     → AdminStudioListPage
/platform/plans       → PlanManagementPage
/platform/subscriptions → SubscriptionOversightPage
/platform/referrals   → PlatformReferralPage
/platform/reports     → IndustryReportsPage
```
Also ensure that after login, admin role redirects to `/platform`, not `/dashboard`.

---

## STEP 8 — Billing and Subscription Flow Verification

This section confirms the Stripe Billing (card subscription) path is working correctly
and that the new cash path integrates cleanly.

### 8.1 Read and verify `BillingPage.tsx`

**Read** `frontend/src/features/billing/components/BillingPage.tsx`.
Confirm it shows: current plan, subscription status, next billing date, and a link to
manage/upgrade. No PayPal references.

### 8.2 Read and verify `CreateSubscriptionCommand` / `CreateSubscriptionValidator`

Locate the command that creates a Stripe Billing subscription
(check `Pena_e_Arte.Application/Billing/Commands/`).
Ensure it:
- Uses `IStripeBillingService` (not `IStripeConnectService`)
- Creates a Stripe Customer if `studio.StripeCustomerId` is null, saves it back
- Returns a `SubscriptionResponse` with `Status`, `CurrentPeriodEnd`, `ClientSecret`
  (for confirming payment on the frontend if required)

If `CreateSubscriptionCommand` doesn't exist, create it:
- Take `PlanId` as input
- Look up the `Plan` entity to get the Stripe price
- Call `IStripeBillingService.CreateSubscriptionAsync(customerId, priceId)`
- Persist result to `Subscription` entity

### 8.3 Read and verify `BillingEndpoints.cs`

Confirm:
- `GET  /api/v1/billing/plans`           → AdminOnly (list plans)
- `POST /api/v1/billing/plans`           → AdminOnly (create plan)
- `PUT  /api/v1/billing/plans/{id}`      → AdminOnly (update plan)
- `DELETE /api/v1/billing/plans/{id}`    → AdminOnly (delete plan)
- `GET  /api/v1/billing/subscription`    → OwnerOnly (get own subscription)
- `POST /api/v1/billing/subscription`    → OwnerOnly (create Stripe subscription)
- `POST /api/webhooks/stripe/billing`    → AllowAnonymous (Stripe webhook)

Fix any endpoint that is missing or misconfigured.

### 8.4 Verify Stripe webhook handlers

Read `HandleInvoicePaidCommand`, `HandleSubscriptionUpdatedCommand`,
`HandleSubscriptionDeletedCommand`.
Ensure they update `Subscription.Status` and `CurrentPeriodEnd` correctly from the
Stripe event. No `StripeAccountId` references.

### 8.5 Verify `billingApi.ts`

**Read** `frontend/src/features/billing/billingApi.ts`.
Ensure endpoints: `getPlans`, `getSubscription`, `createSubscription`,
`createPlan` (AdminOnly), `updatePlan` (AdminOnly), `deletePlan` (AdminOnly).
No PayPal endpoints.

---

## STEP 9 — Tests

### 9.1 Backend integration tests

**Create** `tests/Pena_e_Arte.IntegrationTests/Application/CashPaymentTests.cs`:

```
DeclareCashDeposit_ValidAppointment_CreatesCashPendingPayment
DeclareCashDeposit_DuplicateCall_ThrowsBusinessRuleViolation
ConfirmCashDeposit_CashPending_SetsStatusPaid_AndUpdatesDepositStatus
ConfirmCashDeposit_NotCashPayment_ThrowsBusinessRuleViolation
ConfirmCashDeposit_AlreadyConfirmed_ThrowsBusinessRuleViolation
```

**Create** `tests/Pena_e_Arte.IntegrationTests/Application/SubscriptionActivationTests.cs`:

```
ActivateSubscription_NoExistingSub_CreatesActiveSubscription
ActivateSubscription_GracePeriod_SetsToActive
ActivateSubscription_StudioNotFound_ThrowsNotFoundException
```

**Create** `tests/Pena_e_Arte.IntegrationTests/Application/PlatformStatsTests.cs`:

```
GetPlatformStats_ReturnsAggregateAcrossAllTenants
GetPlatformStats_MrrCalculation_SumsOnlyActiveSubscriptions
```

### 9.2 Frontend tests

**Verify** these test files exist and pass:
```
frontend/src/features/payments/__tests__/PaymentMethodSelector.test.tsx
frontend/src/features/payments/__tests__/CashDepositConfirmButton.test.tsx
```

If they're stubs, fill them in:
- `PaymentMethodSelector`: renders card tab by default; switches to cash; card tab shows
  Stripe PaymentElement when clientSecret present; cash tab calls `declareCashDeposit`
- `CashDepositConfirmButton`: shows button; clicking shows confirm prompt; confirming
  calls `confirmCashDeposit`; cancel returns to initial state

---

## STEP 10 — Final Build and Verification

```bash
# Backend
dotnet build
dotnet test

# Frontend
cd frontend
pnpm tsc --noEmit
pnpm lint
pnpm test
```

Fix ALL errors before stopping. Do not leave compilation failures.

### Final checklist (verify each before committing):

- [ ] `dotnet build` — zero errors, zero warnings on modified files
- [ ] `dotnet test` — all tests pass
- [ ] `pnpm tsc --noEmit` — zero type errors
- [ ] `pnpm test` — all tests pass
- [ ] No `RequestOptions { StripeAccount = ... }` anywhere in the codebase (`grep -r "StripeAccount"`)
- [ ] No `PayPal` anywhere except in `payment-fallback-prompt.md` (`grep -r "PayPal" --include="*.cs" --include="*.ts" --include="*.tsx"`)
- [ ] No `ConnectStudio` anywhere (`grep -r "ConnectStudio"`)
- [ ] `ClientPaymentMethod` enum is `Card | Cash`
- [ ] `PaymentStatus` enum includes `CashPending`
- [ ] `Payment` entity has `Method`, `CashNote`, `CashConfirmedByUserId`
- [ ] EF migration applied: `AddCashPaymentFields`
- [ ] EF migration applied: `RemoveStripeConnect`
- [ ] `POST /api/v1/payments/cash` exists and requires `ClientAndAbove`
- [ ] `POST /api/v1/payments/{id}/cash/confirm` exists and requires `ArtistAndAbove`
- [ ] `POST /api/v1/platform/studios/{studioId}/subscription/activate` exists and requires `AdminOnly`
- [ ] `GET /api/v1/platform/stats` exists and requires `AdminOnly`
- [ ] `GET /api/v1/platform/subscriptions` exists and requires `AdminOnly`
- [ ] `PATCH /api/v1/platform/subscriptions/{studioId}/trial` exists and requires `AdminOnly`
- [ ] `GET /api/v1/platform/referral-codes` exists and requires `AdminOnly`
- [ ] `PATCH /api/v1/platform/referral-codes/{id}/deactivate` exists and requires `AdminOnly`
- [ ] `AdminDashboardPage` shows KPI cards, at-risk widget, quick nav
- [ ] `SubscriptionOversightPage` has "Extend trial" and "Activate (cash)" actions
- [ ] `PaymentMethodSelector` has Card + Cash tabs only
- [ ] `DashboardPage` has cash-pending section
- [ ] `SubscribePage` has cash info block
- [ ] Admin login redirects to `/platform`
- [ ] No PII in any log line
- [ ] `VITE_STRIPE_PUBLISHABLE_KEY` in `.env.example` (placeholder only)
- [ ] `Stripe:SecretKey` referenced only via config key, not hardcoded

---

## Reference Files

Read these for complete implementation specs — do not guess, read them:

```
docs/claude/payment-simplified-prompt.md   ← Card/Cash implementation details
docs/claude/admin-dashboard-prompt.md     ← Platform features F1–F6 + FIX
docs/claude/architecture.md                ← IgnoreQueryFilters approved usages
docs/claude/backend.md                     ← Patterns for commands, validators, endpoints
docs/claude/frontend.md                    ← RTK Query patterns, store setup, routing
docs/claude/conventions.md                 ← Naming, formatting rules
```
