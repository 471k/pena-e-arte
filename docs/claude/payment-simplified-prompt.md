# Payment Simplification — Card & Cash Only
## Overnight Execution Prompt

> **This prompt SUPERSEDES `payment-fallback-prompt.md` entirely.**
> If any code from that file was already implemented, it must be rolled back or removed
> as described in Section 1 before adding anything new.
>
> Read `CLAUDE.md`, `docs/claude/backend.md`, `docs/claude/frontend.md`,
> `docs/claude/database.md`, and `docs/claude/conventions.md` before starting.
> Execute sections in order.

---

## Payment Model (Final)

Two payment flows, two methods each:

```
CLIENT DEPOSITS (client → studio, at booking)
  Card  → Stripe Payment Element (platform aggregator account, no Connect)
  Cash  → client declares intent; owner confirms receipt in the dashboard

PLATFORM SUBSCRIPTIONS (owner → platform, SaaS access)
  Card  → Stripe Billing (existing, unchanged)
  Cash  → owner contacts issuer; issuer activates subscription manually
```

No PayPal. No Stripe Connect. No payout services. No wallet providers.
Stripe is used only for card collection. Cash is recorded manually.

---

## Global Rules

- No business logic in endpoints — MediatR only.
- Every DB query on tenant data through EF Core global query filters.
- No PII in logs. Structured Serilog with `@` prefix on all properties.
- TypeScript strict mode. No `any`. Named exports only on components.
- No `useEffect` for data fetching — RTK Query only.
- Write tests alongside every handler.

---

## SECTION 1 — Remove All PayPal Artifacts

Work through this removal checklist completely before writing any new code.

### 1.1 Delete PayPal service files

Delete the entire directory if it exists:
```
Pena_e_Arte.Infrastructure/Services/PayPal/
```
This removes `PayPalOptions.cs`, `PayPalTokenCache.cs`,
`PayPalCheckoutService.cs`, and `PayPalPayoutService.cs`.

### 1.2 Remove PayPal interfaces from Domain

Delete if they exist:
```
Pena_e_Arte.Domain/Interfaces/IPayPalCheckoutService.cs
Pena_e_Arte.Domain/Interfaces/IPayPalPayoutService.cs
```
Also delete `PayPalOrderResult`, `PayPalCaptureResult`, `PayPalPayoutResult`
records if they were placed in those files.

### 1.3 Remove PayPal entities from Domain

Delete if they exist:
```
Pena_e_Arte.Domain/Entities/StudioPayoutMethod.cs
Pena_e_Arte.Domain/Entities/StudioPayout.cs
```

### 1.4 Remove PayPal enums from Domain

Delete if they exist:
```
Pena_e_Arte.Domain/Enums/PayoutMethodType.cs
Pena_e_Arte.Domain/Enums/PayoutStatus.cs
```

### 1.5 Remove PayPal Application commands

Delete if they exist:
```
Pena_e_Arte.Application/Payments/Commands/CreatePayPalOrderCommand.cs
Pena_e_Arte.Application/Payments/Commands/CapturePayPalOrderCommand.cs
Pena_e_Arte.Application/Payments/Validators/CreatePayPalOrderValidator.cs
Pena_e_Arte.Application/Payments/Validators/CapturePayPalOrderValidator.cs
Pena_e_Arte.Application/Payouts/           (entire folder)
```

### 1.6 Remove PayPal Contracts

Delete if they exist:
```
Pena_e_Arte.Contracts/Requests/CreatePayPalOrderRequest.cs
Pena_e_Arte.Contracts/Requests/CapturePayPalOrderRequest.cs
Pena_e_Arte.Contracts/Requests/UpsertPayoutMethodRequest.cs
Pena_e_Arte.Contracts/Requests/InitiatePayoutRequest.cs
Pena_e_Arte.Contracts/Responses/PayPalOrderResponse.cs
Pena_e_Arte.Contracts/Responses/PayoutMethodResponse.cs
Pena_e_Arte.Contracts/Responses/StudioPayoutResponse.cs
```

### 1.7 Remove PayPal API endpoints

**Edit** `Pena_e_Arte.API/Endpoints/PaymentEndpoints.cs`:
Remove `CreatePayPalOrder` and `CapturePayPalOrder` endpoint registrations and
their private handler methods.

Delete if it exists:
```
Pena_e_Arte.API/Endpoints/PayoutEndpoints.cs
```

Remove `app.MapPayoutEndpoints()` from `Program.cs` if present.

### 1.8 Remove PayPal EF Core configurations

Delete if they exist:
```
Pena_e_Arte.Infrastructure/Persistence/Configurations/StudioPayoutMethodConfiguration.cs
Pena_e_Arte.Infrastructure/Persistence/Configurations/StudioPayoutConfiguration.cs
```

**Edit** `AppDbContext.cs` — remove if present:
```csharp
public DbSet<StudioPayoutMethod> StudioPayoutMethods { get; set; }
public DbSet<StudioPayout>       StudioPayouts       { get; set; }
```

### 1.9 Remove PayPal DI registrations

**Edit** the `ServiceCollectionExtensions` file where PayPal services were registered.
Remove:
- `services.Configure<PayPalOptions>(...)`
- `services.AddSingleton<PayPalTokenCache>()`
- `services.AddHttpClient("PayPal", ...)`
- `services.AddScoped<IPayPalCheckoutService, PayPalCheckoutService>()`
- `services.AddScoped<IPayPalPayoutService, PayPalPayoutService>()`

### 1.10 Handle the migration

**If the PayPal migration (`AddPayPalPaymentAndPayoutEntities`) has NOT been applied
to the database yet:**
Delete the migration file and snapshot changes:
```bash
dotnet ef migrations remove \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
```

**If the migration HAS been applied:**
Create a rollback migration:
```bash
dotnet ef migrations add RemovePayPalEntities \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
dotnet ef database update \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
```

The rollback migration should drop `StudioPayoutMethods`, `StudioPayouts` tables
and remove `PayPalOrderId`, `PayPalCaptureId` columns from `Payments`.

### 1.11 Remove PayPal from Payment entity

**Edit** `Pena_e_Arte.Domain/Entities/Payment.cs`:
Remove `PayPalOrderId` and `PayPalCaptureId` properties if present.

### 1.12 Frontend — remove PayPal package and provider

```bash
cd frontend
pnpm remove @paypal/react-paypal-js
```

**Edit** `frontend/src/main.tsx`:
Remove `PayPalScriptProvider` import and wrapper if present.

Remove from `frontend/.env.local` and `frontend/.env.example`:
```
VITE_PAYPAL_CLIENT_ID=...
```

Delete if it exists:
```
frontend/src/features/studios/components/PayoutMethodSettings.tsx
```

Remove PayPal-related RTK Query endpoints from `paymentsApi.ts` if present:
`createPayPalOrder`, `capturePayPalOrder`, `getPayoutMethod`,
`upsertPayoutMethod`, `getPayouts`, `initiatePayout`.

Remove PayPal tag types `"PayoutMethod"`, `"Payout"` from `paymentsApi`.

Remove PayPal-related types from `payment.types.ts`:
`PayPalOrderResponse`, `PayoutMethodResponse`, `StudioPayoutResponse`.

---

## SECTION 2 — Domain Changes

### 2.1 Update `ClientPaymentMethod` enum

**Replace** `Pena_e_Arte.Domain/Enums/ClientPaymentMethod.cs` entirely:

```csharp
namespace Pena_e_Arte.Domain.Enums;

public enum ClientPaymentMethod
{
    Card,
    Cash,
}
```

### 2.2 Update `PaymentStatus` enum

**Replace** `Pena_e_Arte.Domain/Enums/PaymentStatus.cs` entirely:

```csharp
namespace Pena_e_Arte.Domain.Enums;

public enum PaymentStatus
{
    /// <summary>Card payment intent created, awaiting client action.</summary>
    Pending,

    /// <summary>Client selected cash; awaiting owner confirmation of receipt.</summary>
    CashPending,

    /// <summary>Card deposit authorised (held), not yet captured.</summary>
    Captured,

    /// <summary>Payment fully received — card captured or cash confirmed.</summary>
    Paid,

    /// <summary>Payment refunded.</summary>
    Refunded,

    /// <summary>Card payment failed.</summary>
    Failed,
}
```

### 2.3 Update `Payment` entity

**Replace** `Pena_e_Arte.Domain/Entities/Payment.cs` entirely:

```csharp
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class Payment : TenantEntity
{
    public Guid              AppointmentId        { get; set; }
    public Guid              ClientId             { get; set; }
    public decimal           Amount               { get; set; }
    public PaymentStatus     Status               { get; set; } = PaymentStatus.Pending;
    public ClientPaymentMethod Method             { get; set; } = ClientPaymentMethod.Card;

    // Card (Stripe) fields — null for cash payments
    public string? StripePaymentIntentId         { get; set; }
    public string? ClientSecret                  { get; set; }

    // Cash fields
    public string?   CashNote                    { get; set; }
    public Guid?     CashConfirmedByUserId        { get; set; }

    public DateTime? PaidAt                      { get; set; }

    public Appointment Appointment               { get; set; } = null!;
    public Client      Client                    { get; set; } = null!;
    public ICollection<SessionSplit> SessionSplits { get; set; } = [];
}
```

---

## SECTION 3 — Infrastructure Changes

### 3.1 Keep `StripePaymentService` as-is (aggregator model)

The aggregator fix from `payment-fallback-prompt.md` Section 2.1 is correct and stays.
`StripePaymentService` must have **zero** `RequestOptions { StripeAccount = ... }` calls.
Verify this is the case. If the original connected-account version is still in place,
apply the fix now:

**Replace** `Pena_e_Arte.Infrastructure/Services/StripePaymentService.cs`:

```csharp
using Pena_e_Arte.Domain.Interfaces;
using Stripe;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Aggregator model: all card charges collected into the platform's own Stripe account.
/// No StripeAccount (connected account) header — Stripe Connect is not used.
/// </summary>
public class StripePaymentService(PaymentIntentService intentService, RefundService refundService)
    : IStripePaymentService
{
    public async Task<(string PaymentIntentId, string ClientSecret)> CreatePaymentIntentAsync(
        long amountInCents, string currency, Guid paymentId, CancellationToken ct)
    {
        PaymentIntentCreateOptions options = new()
        {
            Amount        = amountInCents,
            Currency      = currency.ToLowerInvariant(),
            CaptureMethod = "manual",
            Metadata      = new Dictionary<string, string>
            {
                { "payment_id", paymentId.ToString() }
            },
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true,
            },
        };

        // No RequestOptions.StripeAccount — platform account only (aggregator model)
        PaymentIntent intent = await intentService.CreateAsync(options, null, ct);
        return (intent.Id, intent.ClientSecret!);
    }

    public async Task CapturePaymentAsync(string paymentIntentId, CancellationToken ct)
    {
        await intentService.CaptureAsync(paymentIntentId, null, null, ct);
    }

    public async Task<string> RefundPaymentIntentAsync(
        string paymentIntentId, long? amountInCents, CancellationToken ct)
    {
        RefundCreateOptions options = new()
        {
            PaymentIntent = paymentIntentId,
            Amount        = amountInCents,
        };

        Refund refund = await refundService.CreateAsync(options, null, ct);
        return refund.Id;
    }
}
```

### 3.2 Update `IStripePaymentService` interface

**Replace** `Pena_e_Arte.Domain/Interfaces/IStripePaymentService.cs`:

```csharp
namespace Pena_e_Arte.Domain.Interfaces;

public interface IStripePaymentService
{
    Task<(string PaymentIntentId, string ClientSecret)> CreatePaymentIntentAsync(
        long amountInCents, string currency, Guid paymentId, CancellationToken ct);

    Task CapturePaymentAsync(string paymentIntentId, CancellationToken ct);

    Task<string> RefundPaymentIntentAsync(
        string paymentIntentId, long? amountInCents, CancellationToken ct);
}
```

No `connectedAccountId` parameter anywhere.

### 3.3 EF Core — Payment configuration update

**Edit** `Pena_e_Arte.Infrastructure/Persistence/Configurations/PaymentConfiguration.cs`
to include the new cash columns:

```csharp
builder.Property(x => x.Method).HasConversion<string>().HasMaxLength(10);
builder.Property(x => x.CashNote).HasMaxLength(500);
```

### 3.4 Generate migration

```bash
dotnet ef migrations add SimplifyPaymentToCashAndCard \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
dotnet ef database update \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
```

Review the generated migration. It should:
- Add `Method` column (string, max 10) to `Payments` if not already present
- Add `CashNote` column (string, nullable, max 500) to `Payments`
- Add `CashConfirmedByUserId` column (Guid, nullable) to `Payments`
- NOT touch `StripePaymentIntentId` or `ClientSecret`

---

## SECTION 4 — Application Layer

### 4.1 Update `CreatePaymentIntentCommand`

**Edit** the existing handler. Remove any lookup for `studio.StripeAccountId`.
The handler must call:

```csharp
(string intentId, string secret) = await _stripe.CreatePaymentIntentAsync(
    amountInCents: (long)(depositAmount * 100),
    currency:      "EUR",
    paymentId:     payment.Id,
    ct);

payment.StripePaymentIntentId = intentId;
payment.ClientSecret          = secret;
payment.Method                = ClientPaymentMethod.Card;
payment.Status                = PaymentStatus.Pending;
```

No reference to `connectedAccountId`, `StripeAccountId`, or `RequestOptions`.

### 4.2 New Command: `DeclareCashDepositCommand`

Called by the client at booking time to signal they will pay cash.
Creates the `Payment` record with `Method = Cash` and `Status = CashPending`.

**Create** `Pena_e_Arte.Application/Payments/Commands/DeclareCashDepositCommand.cs`:

```csharp
using MediatR;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

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
            ?? throw new AppointmentNotFoundException(command.AppointmentId);

        // Prevent duplicate cash declarations
        bool alreadyExists = await db.Payments
            .AnyAsync(p => p.AppointmentId == command.AppointmentId, ct);
        if (alreadyExists)
            throw new DomainException("A payment record already exists for this appointment.");

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

        return payment.ToResponse();
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

### 4.3 New Command: `ConfirmCashDepositCommand`

Called by an artist or owner to confirm that the client physically paid cash.

**Create** `Pena_e_Arte.Application/Payments/Commands/ConfirmCashDepositCommand.cs`:

```csharp
using MediatR;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Commands;

public record ConfirmCashDepositCommand(Guid PaymentId) : IRequest<PaymentResponse>;

public class ConfirmCashDepositHandler(AppDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ConfirmCashDepositCommand, PaymentResponse>
{
    public async Task<PaymentResponse> Handle(
        ConfirmCashDepositCommand command, CancellationToken ct)
    {
        Payment payment = await db.Payments
            .FirstOrDefaultAsync(p => p.Id == command.PaymentId, ct)
            ?? throw new PaymentNotFoundException(command.PaymentId);

        if (payment.Method != ClientPaymentMethod.Cash)
            throw new DomainException("This payment is not a cash payment.");

        if (payment.Status != PaymentStatus.CashPending)
            throw new DomainException("This cash payment has already been confirmed.");

        payment.Status                 = PaymentStatus.Paid;
        payment.PaidAt                 = DateTime.UtcNow;
        payment.CashConfirmedByUserId  = currentUser.UserId;

        // Mirror onto the Appointment's DepositStatus
        Appointment? appt = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == payment.AppointmentId, ct);
        if (appt is not null)
            appt.DepositStatus = DepositStatus.Paid;

        await db.SaveChangesAsync(ct);
        return payment.ToResponse();
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

### 4.4 New Command: `ActivateSubscriptionManuallyCommand`

Issuer records a cash subscription payment and activates the studio.

**Create** `Pena_e_Arte.Application/Billing/Commands/ActivateSubscriptionManuallyCommand.cs`:

```csharp
using MediatR;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

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
            ?? throw new StudioNotFoundException(command.StudioId);

        Plan plan = await db.Plans
            .FirstOrDefaultAsync(p => p.Id == command.PlanId, ct)
            ?? throw new PlanNotFoundException(command.PlanId);

        if (studio.Subscription is null)
        {
            studio.Subscription = new Subscription
            {
                StudioId        = studio.Id,
                PlanId          = plan.Id,
                Status          = SubscriptionStatus.Active,
                CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1),
                TrialExpiresAt  = studio.TrialExpiresAt,
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
            "Subscription manually activated for studio {@StudioId} on plan {@PlanId}. Note: {@Note}",
            studio.Id, plan.Id, command.Note);

        await db.SaveChangesAsync(ct);
        return studio.Subscription.ToResponse();
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

---

## SECTION 5 — Contracts

**Create** `Pena_e_Arte.Contracts/Requests/DeclareCashDepositRequest.cs`:

```csharp
public record DeclareCashDepositRequest(Guid AppointmentId, string? Note);
```

**Create** `Pena_e_Arte.Contracts/Requests/ActivateSubscriptionManuallyRequest.cs`:

```csharp
public record ActivateSubscriptionManuallyRequest(Guid PlanId, string? Note);
```

**Update** `Pena_e_Arte.Contracts/Responses/PaymentResponse.cs` — ensure it includes:

```csharp
public record PaymentResponse(
    Guid    Id,
    Guid    AppointmentId,
    decimal Amount,
    string  Status,      // PaymentStatus.ToString()
    string  Method,      // ClientPaymentMethod.ToString()
    string? StripePaymentIntentId,
    string? ClientSecret,
    string? CashNote,
    DateTime? PaidAt);
```

---

## SECTION 6 — API Layer

### 6.1 Update `PaymentEndpoints.cs`

Add the two new cash endpoints. Keep all existing card endpoints untouched.

```csharp
// Cash deposit — client declares intent (ClientAndAbove)
group.MapPost("/cash",
    DeclareCashDeposit).RequireAuthorization("ClientAndAbove");

// Cash confirmation — studio staff confirms receipt (ArtistAndAbove)
group.MapPost("/{id:guid}/cash/confirm",
    ConfirmCashDeposit).RequireAuthorization("ArtistAndAbove");

private static async Task<IResult> DeclareCashDeposit(
    DeclareCashDepositRequest request,
    ISender                   mediator,
    CancellationToken         ct)
{
    PaymentResponse result = await mediator.Send(
        new DeclareCashDepositCommand(request.AppointmentId, request.Note), ct);
    return Results.Created($"/api/v1/payments/{result.Id}", result);
}

private static async Task<IResult> ConfirmCashDeposit(
    Guid              id,
    ISender           mediator,
    CancellationToken ct)
{
    PaymentResponse result = await mediator.Send(new ConfirmCashDepositCommand(id), ct);
    return Results.Ok(result);
}
```

### 6.2 Update `PlatformEndpoints.cs`

Add the manual subscription activation endpoint under `IssuerOnly`:

```csharp
group.MapPost("studios/{studioId:guid}/subscription/activate",
    ActivateSubscriptionManually);

private static async Task<IResult> ActivateSubscriptionManually(
    Guid                                  studioId,
    ActivateSubscriptionManuallyRequest   request,
    ISender                               mediator,
    CancellationToken                     ct)
{
    SubscriptionResponse result = await mediator.Send(
        new ActivateSubscriptionManuallyCommand(studioId, request.PlanId, request.Note), ct);
    return Results.Ok(result);
}
```

---

## SECTION 7 — Frontend

### 7.1 Update `payment.types.ts`

Replace the file with the clean version — no PayPal types:

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

### 7.2 Update `paymentsApi.ts`

Remove all PayPal-related endpoints. Add cash endpoints:

```typescript
tagTypes: ["Payment"],  // only this tag type — remove PayoutMethod, Payout

// Add:
declareCashDeposit: builder.mutation<PaymentResponse, { appointmentId: string; note?: string }>({
  query: (body) => ({ url: "payments/cash", method: "POST", body }),
  invalidatesTags: ["Payment"],
}),
confirmCashDeposit: builder.mutation<PaymentResponse, string>({
  query: (id) => ({ url: `payments/${id}/cash/confirm`, method: "POST" }),
  invalidatesTags: ["Payment"],
}),
```

Export `useDeclareCashDepositMutation` and `useConfirmCashDepositMutation`.

### 7.3 Replace `PaymentMethodSelector`

**Replace** `frontend/src/features/payments/components/PaymentMethodSelector.tsx`:

This component is the single source of truth for all client-facing payment UI.

```tsx
import { useState } from "react";
import { loadStripe } from "@stripe/stripe-js";
import {
  Elements,
  PaymentElement,
  useStripe,
  useElements,
} from "@stripe/react-stripe-js";
import { Banknote, CreditCard, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { cn } from "@/shared/utils/cn";
import {
  useGetPaymentClientSecretQuery,
  useDeclareCashDepositMutation,
} from "@/features/payments/paymentsApi";

const stripePromise = loadStripe(import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY);

type Tab = "card" | "cash";

interface PaymentMethodSelectorProps {
  paymentId:     string;
  appointmentId: string;
  amount:        number;
  onSuccess:     () => void;
  onError:       (message: string) => void;
}

// ── Card tab ──────────────────────────────────────────────────────────────

function CardCheckoutForm({
  onSuccess,
  onError,
}: Pick<PaymentMethodSelectorProps, "onSuccess" | "onError">) {
  const stripe      = useStripe();
  const elements    = useElements();
  const [busy, setBusy] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!stripe || !elements) return;
    setBusy(true);
    const { error } = await stripe.confirmPayment({
      elements,
      confirmParams: {
        return_url: `${window.location.origin}/booking/success`,
      },
      redirect: "if_required",
    });
    setBusy(false);
    if (error) onError(error.message ?? "Card payment failed.");
    else        onSuccess();
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <PaymentElement />
      <Button type="submit" className="w-full" disabled={busy || !stripe}>
        {busy
          ? <><Loader2 className="h-4 w-4 animate-spin mr-2" />Processing…</>
          : "Pay by card"}
      </Button>
    </form>
  );
}

// ── Cash tab ──────────────────────────────────────────────────────────────

function CashInfoPanel({
  appointmentId,
  amount,
  onSuccess,
  onError,
}: Pick<PaymentMethodSelectorProps, "appointmentId" | "amount" | "onSuccess" | "onError">) {
  const [declareCash, { isLoading }] = useDeclareCashDepositMutation();

  async function handleSelect() {
    try {
      await declareCash({ appointmentId }).unwrap();
      onSuccess();
    } catch {
      onError("Could not register cash payment. Please try again.");
    }
  }

  return (
    <div className="space-y-4">
      <div className="rounded-lg border border-input bg-muted/50 p-4 space-y-2 text-sm">
        <p className="font-medium">Pay at the studio</p>
        <p className="text-muted-foreground">
          Your deposit of <span className="font-medium text-foreground">
            €{amount.toFixed(2)}
          </span> will be collected in cash when you arrive.
          Your booking will be held as pending until the studio confirms receipt.
        </p>
        <p className="text-muted-foreground text-xs">
          The studio may contact you to confirm your appointment before your visit.
        </p>
      </div>
      <Button className="w-full" onClick={handleSelect} disabled={isLoading}>
        {isLoading
          ? <><Loader2 className="h-4 w-4 animate-spin mr-2" />Saving…</>
          : "Confirm — I'll pay cash at the studio"}
      </Button>
    </div>
  );
}

// ── Main ──────────────────────────────────────────────────────────────────

export function PaymentMethodSelector({
  paymentId,
  appointmentId,
  amount,
  onSuccess,
  onError,
}: PaymentMethodSelectorProps) {
  const [tab, setTab] = useState<Tab>("card");
  const { data: secretData } = useGetPaymentClientSecretQuery(paymentId);

  const tabClass = (active: boolean) =>
    cn(
      "flex items-center gap-2 flex-1 justify-center py-2.5 rounded-md text-sm font-medium transition-colors",
      active
        ? "bg-background text-foreground shadow-sm"
        : "text-muted-foreground hover:text-foreground"
    );

  return (
    <div className="space-y-4">
      {/* Tab bar */}
      <div className="flex gap-1 rounded-lg bg-muted p-1">
        <button type="button" className={tabClass(tab === "card")} onClick={() => setTab("card")}>
          <CreditCard className="h-4 w-4" />
          Card
        </button>
        <button type="button" className={tabClass(tab === "cash")} onClick={() => setTab("cash")}>
          <Banknote className="h-4 w-4" />
          Cash
        </button>
      </div>

      {/* Card tab */}
      {tab === "card" && secretData?.clientSecret && (
        <Elements
          stripe={stripePromise}
          options={{
            clientSecret: secretData.clientSecret,
            appearance:   { theme: "stripe" },
          }}
        >
          <CardCheckoutForm onSuccess={onSuccess} onError={onError} />
        </Elements>
      )}

      {tab === "card" && !secretData?.clientSecret && (
        <div className="flex items-center justify-center py-8 text-muted-foreground gap-2">
          <Loader2 className="h-4 w-4 animate-spin" />
          <span className="text-sm">Loading payment form…</span>
        </div>
      )}

      {/* Cash tab */}
      {tab === "cash" && (
        <CashInfoPanel
          appointmentId={appointmentId}
          amount={amount}
          onSuccess={onSuccess}
          onError={onError}
        />
      )}
    </div>
  );
}
```

### 7.4 New Component: `CashDepositConfirmButton`

Owner/artist dashboard component — shown for each `CashPending` payment.

**Create** `frontend/src/features/payments/components/CashDepositConfirmButton.tsx`:

```tsx
import { useState } from "react";
import { Banknote, Check, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { useConfirmCashDepositMutation } from "@/features/payments/paymentsApi";

interface CashDepositConfirmButtonProps {
  paymentId:    string;
  clientName:   string;
  amount:       number;
}

export function CashDepositConfirmButton({
  paymentId,
  clientName,
  amount,
}: CashDepositConfirmButtonProps) {
  const [confirm, setConfirm]       = useState(false);
  const [confirmCash, { isLoading }] = useConfirmCashDepositMutation();

  async function handleConfirm() {
    await confirmCash(paymentId);
    setConfirm(false);
  }

  if (confirm) {
    return (
      <div className="flex items-center gap-2">
        <span className="text-xs text-muted-foreground">
          Confirm €{amount.toFixed(2)} cash received from {clientName}?
        </span>
        <Button
          size="sm"
          className="h-7 px-2 text-xs gap-1"
          disabled={isLoading}
          onClick={handleConfirm}
        >
          {isLoading
            ? <Loader2 className="h-3 w-3 animate-spin" />
            : <><Check className="h-3 w-3" /> Yes</>}
        </Button>
        <Button
          size="sm"
          variant="ghost"
          className="h-7 px-2 text-xs"
          onClick={() => setConfirm(false)}
        >
          Cancel
        </Button>
      </div>
    );
  }

  return (
    <Button
      size="sm"
      variant="outline"
      className="h-7 px-2 text-xs gap-1"
      onClick={() => setConfirm(true)}
    >
      <Banknote className="h-3.5 w-3.5" />
      Mark cash received
    </Button>
  );
}
```

### 7.5 Add Cash-Pending Section to `DashboardPage`

**Edit** `frontend/src/features/dashboard/components/DashboardPage.tsx`:

Add a `CashPendingSection` component below `TodaySection`. It queries
`useGetPaymentsQuery()` (existing endpoint) and filters to `status === "CashPending"`.

Each row shows: client name (from artist list lookup), amount, and the
`<CashDepositConfirmButton />`. Only show the section if there is at least one
cash-pending payment. Title: "Awaiting Cash" with a `Banknote` icon.

### 7.6 Add `platformApi` Subscription Activation

**Edit** `frontend/src/features/platform/platformApi.ts` — add:

```typescript
activateSubscriptionManually: builder.mutation<
  SubscriptionResponse,
  { studioId: string; planId: string; note?: string }
>({
  query: ({ studioId, ...body }) => ({
    url:    `platform/studios/${studioId}/subscription/activate`,
    method: "POST",
    body,
  }),
  invalidatesTags: ["PlatformSubscription", "PlatformStats"],
}),
```

Export `useActivateSubscriptionManuallyMutation`.

### 7.7 Update `SubscriptionOversightPage`

**Edit** `frontend/src/features/platform/components/SubscriptionOversightPage.tsx`:

For studios with status `"NoSubscription"` or `"GracePeriod"` or `"Cancelled"`,
add an "Activate (cash)" button alongside the existing "Extend trial" button.

Clicking it opens an inline form with:
- A plan selector (`<select>` populated from `useGetIssuerPlansQuery()`)
- An optional note input
- Confirm button → calls `useActivateSubscriptionManuallyMutation`

Label it clearly: **"Activate — Cash Payment"** so the issuer knows this bypasses Stripe.

### 7.8 Update `SubscribePage`

**Edit** `frontend/src/features/billing/components/SubscribePage.tsx`:

After the existing Stripe card subscription form, add a section:

```tsx
<div className="mt-6 rounded-lg border border-input p-4 space-y-2 text-sm">
  <p className="font-medium flex items-center gap-2">
    <Banknote className="h-4 w-4" />
    Prefer to pay cash?
  </p>
  <p className="text-muted-foreground">
    Contact us and we'll activate your subscription once payment is confirmed.
    Your trial continues until then.
  </p>
  <a
    href="mailto:contact@penaearte.com"
    className="text-sm font-medium underline underline-offset-4"
  >
    Get in touch
  </a>
</div>
```

Replace `contact@penaearte.com` with the actual platform contact email from config
(read from `import.meta.env.VITE_CONTACT_EMAIL`). Add `VITE_CONTACT_EMAIL` to
`.env.example` and `.env.local`.

---

## SECTION 8 — Tests

### Backend

**Create** `tests/Pena_e_Arte.IntegrationTests/Application/CashPaymentIntegrationTests.cs`:

```
DeclareCashDeposit_ValidAppointment_CreatesCashPendingPayment
DeclareCashDeposit_DuplicateDeclaration_ThrowsDomainException
ConfirmCashDeposit_CashPendingPayment_SetsStatusPaidAndUpdatesDeposit
ConfirmCashDeposit_NotCashPayment_ThrowsDomainException
ConfirmCashDeposit_AlreadyConfirmed_ThrowsDomainException
ActivateSubscriptionManually_NoExistingSubscription_CreatesActiveSubscription
ActivateSubscriptionManually_GracePeriodSubscription_SetsToActive
ActivateSubscriptionManually_StudioNotFound_ThrowsStudioNotFoundException
```

**Create** `tests/Pena_e_Arte.UnitTests/Services/StripePaymentServiceAggregatorTests.cs`:

```
CreatePaymentIntent_DoesNotPassConnectedAccount_SucceedsWithPlatformAccount
// Use Moq or NSubstitute to verify no RequestOptions with StripeAccount is sent
```

### Frontend

**Create** `frontend/src/features/payments/__tests__/PaymentMethodSelector.test.tsx`:

```typescript
// renders card tab by default
// switches to cash tab on click
// card tab renders Stripe PaymentElement when clientSecret present
// card tab shows loading spinner when no clientSecret
// cash tab shows "pay at studio" info panel
// cash tab confirm button calls declareCashDeposit mutation
// cash tab shows error on mutation failure
```

**Create** `frontend/src/features/payments/__tests__/CashDepositConfirmButton.test.tsx`:

```typescript
// shows "Mark cash received" button initially
// clicking shows confirmation prompt with client name and amount
// confirming calls confirmCashDeposit with correct paymentId
// cancel returns to initial state
```

---

## SECTION 9 — Environment Variables (Final)

`.env.local` (gitignored, frontend):
```
VITE_STRIPE_PUBLISHABLE_KEY=pk_test_...
VITE_CONTACT_EMAIL=contact@penaearte.com
```

`.env.example` (committed, no real values):
```
VITE_STRIPE_PUBLISHABLE_KEY=pk_test_replace_me
VITE_CONTACT_EMAIL=your_contact_email_here
```

`appsettings.Development.json` (gitignored, backend):
```json
{
  "Stripe": {
    "PublishableKey":        "pk_test_...",
    "SecretKey":             "sk_test_...",
    "WebhookSecretBilling":  "whsec_...",
    "WebhookSecretConnect":  "whsec_..."
  }
}
```

No PayPal config section needed anywhere.

---

## Final Checklist

Before committing:

- [ ] All PayPal files, services, interfaces, entities, enums, and endpoints deleted (Section 1).
- [ ] `@paypal/react-paypal-js` removed from `package.json` and `node_modules`.
- [ ] `ClientPaymentMethod` enum is `Card | Cash` only.
- [ ] `PaymentStatus` includes `CashPending` and `Paid` (card capture flow uses `Captured` → `Paid`).
- [ ] `Payment` entity has `CashNote` and `CashConfirmedByUserId`; no PayPal columns.
- [ ] `StripePaymentService` has zero `RequestOptions { StripeAccount = ... }` calls.
- [ ] `DeclareCashDepositCommand` creates `CashPending` payment.
- [ ] `ConfirmCashDepositCommand` sets payment to `Paid` and mirrors `DepositStatus.Paid` on appointment.
- [ ] `ActivateSubscriptionManuallyCommand` is `IssuerOnly`.
- [ ] New payment endpoints added: `POST /api/v1/payments/cash`, `POST /api/v1/payments/{id}/cash/confirm`.
- [ ] New platform endpoint added: `POST /api/v1/platform/studios/{studioId}/subscription/activate`.
- [ ] `PaymentMethodSelector` shows Card and Cash tabs only — no PayPal tab.
- [ ] `CashDepositConfirmButton` in owner dashboard for `CashPending` payments.
- [ ] `SubscriptionOversightPage` has "Activate (cash)" button for eligible studios.
- [ ] `SubscribePage` has cash contact info section.
- [ ] Migration applied; `Payments` table has `Method`, `CashNote`, `CashConfirmedByUserId` columns.
- [ ] `dotnet test` passes.
- [ ] `pnpm test` passes.
- [ ] `dotnet build` produces zero warnings on payment-related files.
- [ ] No PII in any log line.
