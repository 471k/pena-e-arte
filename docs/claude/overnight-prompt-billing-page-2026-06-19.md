# Overnight Prompt — Billing Page Overhaul
> Date: 2026-06-19
> Primary target: `BillingPage.tsx`, `BillingPage.test.tsx`, `billingApi.ts`, `billing.types.ts`
> Backend target: new `POST /billing/portal` endpoint (Stripe Customer Portal session)
> No new npm or NuGet packages.

---

## Pre-flight

1. Read `CLAUDE.md`, `docs/claude/frontend.md`, `docs/claude/backend.md`, and
   `docs/claude/architecture.md` before making any changes.
2. Run `pnpm tsc --noEmit` — note pre-existing errors; do not count them as regressions.
3. Run `pnpm test src/features/billing/BillingPage` — confirm all 39 existing tests pass first.
4. Run `dotnet build` — confirm a clean build before touching backend files.

---

## Context

The `BillingPage.tsx` is better than the audit screenshot suggests — it already handles all five
subscription states, studio suspension, pending plan changes, and Stripe Checkout finalization.
The issues are layout/visual polish and two missing sections:

| Category | Issue | Fix |
|---|---|---|
| Layout | Container `max-w-lg` — narrow, off-grid feel | `max-w-2xl` to match other pages |
| Loading | Full-screen spinner; no structural skeleton | `BillingPageSkeleton` matching card layout |
| Status indicator | Colored text only — no background badge | Pill badge with `bg-{color}/10` background |
| Plan display | `"Plan: Starter"` key:value anti-pattern | `<Badge variant="outline">Starter</Badge>` |
| Price | Not shown — `PlanResponse.priceMonthly` exists but isn't displayed | Derive and show `€29/month` |
| Renewal copy | `"Renews 13 Jul 2026"` — amount missing | `"Next charge: €29 on 13 Jul 2026"` |
| "Change plan" button | In page header, disconnected; `outline` variant | Move into plan section; `default` (filled) variant |
| Payment method / invoices / cancel | No path to any of these | Stripe Customer Portal button (one new backend endpoint) |

**What is out of scope for this prompt:**
- Custom invoice list UI — Stripe Customer Portal provides this
- Custom payment method form — Stripe Customer Portal provides this
- Custom cancel subscription UI — Stripe Customer Portal provides this
- Usage meters — no usage endpoint exists

**Already in scope and working — do NOT re-implement:**
- All five subscription states: Trialing, Active, GracePeriod, PastDue, Cancelled ✅
- Studio suspension banner ✅
- Cash vs card-billed distinction ✅
- Pending plan change card with "Keep current plan" action ✅
- Stripe Checkout finalization via `session_id` param ✅
- `refetchOnMountOrArgChange: true` on all queries ✅

---

## Part 1 — `billing.types.ts`

Add one new interface at the bottom of the file:

```ts
export interface BillingPortalResponse {
  url: string;
}
```

---

## Part 2 — `billingApi.ts`

Add one new mutation to the endpoints builder (place it after `cancelPlanChange`):

```ts
// Opens a Stripe Customer Portal session for the owner to manage payment method,
// download invoices, and cancel. Returns a Stripe-hosted URL to redirect to.
createPortalSession: builder.mutation<BillingPortalResponse, { returnUrl: string }>({
  query: (body) => ({ url: "billing/portal", method: "POST", body }),
}),
```

Add `BillingPortalResponse` to the type import at the top:

```ts
import type { SubscriptionResponse, PlanResponse, CreateSubscriptionRequest, BillingPortalResponse } from "./billing.types";
```

Export the new hook at the bottom:

```ts
export const {
  // ... existing exports ...
  useCreatePortalSessionMutation,
} = billingApi;
```

---

## Part 3 — Backend: `POST /billing/portal`

The Stripe Customer Portal gives owners a hosted UI for:
- Viewing and downloading invoices
- Updating payment method
- Cancelling their subscription

This is one endpoint that replaces three separate custom UIs.

### 3a — Application Layer

**File: `Pena_e_Arte.Application/Billing/Commands/CreateBillingPortal/CreateBillingPortalCommand.cs`**

```csharp
namespace Pena_e_Arte.Application.Billing.Commands.CreateBillingPortal;

public sealed record CreateBillingPortalCommand(string ReturnUrl) : IRequest<CreateBillingPortalResult>;

public sealed record CreateBillingPortalResult(string Url);
```

**File: `Pena_e_Arte.Application/Billing/Commands/CreateBillingPortal/CreateBillingPortalValidator.cs`**

```csharp
namespace Pena_e_Arte.Application.Billing.Commands.CreateBillingPortal;

public sealed class CreateBillingPortalValidator : AbstractValidator<CreateBillingPortalCommand>
{
    public CreateBillingPortalValidator()
    {
        RuleFor(x => x.ReturnUrl)
            .NotEmpty()
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .WithMessage("ReturnUrl must be a valid absolute URL.");
    }
}
```

**File: `Pena_e_Arte.Application/Billing/Commands/CreateBillingPortal/CreateBillingPortalHandler.cs`**

```csharp
namespace Pena_e_Arte.Application.Billing.Commands.CreateBillingPortal;

public sealed class CreateBillingPortalHandler(
    AppDbContext          db,
    ICurrentUserContext   currentUser,
    IStripeService        stripeService,  // use whatever IStripeService interface already exists
    ILogger<CreateBillingPortalHandler> logger)
    : IRequestHandler<CreateBillingPortalCommand, CreateBillingPortalResult>
{
    public async Task<CreateBillingPortalResult> Handle(
        CreateBillingPortalCommand request,
        CancellationToken           cancellationToken)
    {
        // Tenant-scoped — global query filters ensure we only see this studio's subscription
        Subscription? sub = await db.Subscriptions
            .Where(s => s.StudioId == currentUser.TenantId)
            .FirstOrDefaultAsync(cancellationToken);

        if (sub is null || sub.StripeCustomerId is null)
            throw new NotFoundException("No active Stripe subscription found.");

        string portalUrl = await stripeService.CreatePortalSessionAsync(
            sub.StripeCustomerId,
            request.ReturnUrl,
            cancellationToken);

        logger.LogInformation(
            "Billing portal session created. tenant_id={TenantId} user_id={UserId} request_id={RequestId}",
            currentUser.TenantId, currentUser.UserId, currentUser.RequestId);

        return new CreateBillingPortalResult(portalUrl);
    }
}
```

**Notes on the handler:**
- If `IStripeService` does not yet have a `CreatePortalSessionAsync` method, add it to the
  interface and its implementation. The Stripe.net call is:
  ```csharp
  var options = new Stripe.BillingPortal.SessionCreateOptions
  {
      Customer  = stripeCustomerId,
      ReturnUrl = returnUrl,
  };
  var session = await _stripeClient.BillingPortal.Sessions.CreateAsync(
      options, cancellationToken: ct);
  return session.Url;
  ```
- If the project uses a different Stripe service abstraction, adapt accordingly — the key is
  passing `customer` and `return_url` to Stripe's `/v1/billing_portal/sessions` endpoint.
- `NotFoundException` — use whatever the project's not-found exception class is called.
- `ICurrentUserContext` / `currentUser.TenantId` — use the actual current-user service pattern
  the codebase uses. Check existing handlers for the correct pattern.

### 3b — API Endpoint

Find the existing billing endpoint group (likely in `Pena_e_Arte.API/Endpoints/BillingEndpoints.cs`
or similar). Add the portal endpoint alongside the existing billing endpoints:

```csharp
group.MapPost("/portal", async (
    CreateBillingPortalRequest req,
    ISender                    sender,
    CancellationToken          ct) =>
{
    CreateBillingPortalResult result = await sender.Send(
        new CreateBillingPortalCommand(req.ReturnUrl), ct);
    return Results.Ok(new { url = result.Url });
})
.RequireAuthorization("OwnerOnly")
.WithName("CreateBillingPortalSession")
.WithTags("Billing");
```

Add the request record near the other billing request models:

```csharp
public sealed record CreateBillingPortalRequest(string ReturnUrl);
```

### 3c — Unit test for the handler

Add a unit test in `Pena_e_Arte.UnitTests` following the project's existing test structure:

```csharp
// CreateBillingPortalHandlerTests.cs
// Tests:
// 1. Returns URL when subscription has StripeCustomerId
// 2. Throws NotFoundException when subscription is null
// 3. Throws NotFoundException when StripeCustomerId is null
```

---

## Part 4 — `BillingPage.tsx` — Structural Changes

### 4a — New imports

Add to the react import (add `useMemo`):

```tsx
import { useEffect, useMemo, useRef } from "react";
```

Add to lucide imports (add `ExternalLink`, `Settings`):

```tsx
import {
  AlertTriangle, Banknote, Calendar, CalendarClock,
  CreditCard, ExternalLink, Loader2, RefreshCw, Settings, ShieldX, Zap,
} from "lucide-react";
```

Add to shadcn imports (add `Badge`, `Skeleton`):

```tsx
import { Badge } from "@/shared/components/ui/badge";
import { Skeleton } from "@/shared/components/ui/skeleton";
```

Import the new mutation:

```tsx
import {
  useGetSubscriptionQuery,
  useGetPlansQuery,
  useCancelPlanChangeMutation,
  useFinalizeCheckoutMutation,
  useCreatePortalSessionMutation,
} from "../billingApi";
```

### 4b — `BillingPageSkeleton` component

Replace the full-screen spinner loading state with a structural skeleton. Add this component
above `BillingPage`:

```tsx
function BillingPageSkeleton() {
  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <CreditCard className="h-5 w-5 text-muted-foreground" />
          <Skeleton className="h-5 w-16" />
        </div>
      </header>
      <main className="max-w-2xl mx-auto px-4 py-8 space-y-4" aria-label="Loading billing information">
        <div className="rounded-xl border bg-card p-5 space-y-3">
          <Skeleton className="h-5 w-20 rounded-full" />
          <Skeleton className="h-4 w-32" />
          <Skeleton className="h-4 w-44" />
          <div className="flex gap-2 pt-1">
            <Skeleton className="h-8 w-28 rounded-md" />
            <Skeleton className="h-8 w-32 rounded-md" />
          </div>
        </div>
      </main>
    </div>
  );
}
```

Update the loading check to return this skeleton:

```tsx
if (loadingSub || loadingPlans) {
  return <BillingPageSkeleton />;
}
```

### 4c — Derive current plan and format price

Add these derived values after the existing `cfg`, `plan`, `canSubscribe`, `isCashBilled`,
`canChangePlan` computations:

```tsx
// Resolve the full PlanResponse so we can show price information
const currentPlan = useMemo<PlanResponse | null>(
  () => (sub.planId && plans ? (plans.find((p) => p.id === sub.planId) ?? null) : null),
  [sub.planId, plans],
);

// Format price in EUR, using the plan's monthly price
function formatEur(cents: number): string {
  // PlanResponse stores prices as whole euros (not cents), adjust if the backend changes
  return new Intl.NumberFormat("pt-PT", {
    style:    "currency",
    currency: "EUR",
    minimumFractionDigits: 0,
  }).format(cents);
}
```

> **Note:** Check whether `PlanResponse.priceMonthly` is stored as whole euros or cents in the
> database. Adjust the `formatEur` division accordingly. The test seed data uses `29` and `49`
> which suggests whole euros. If a shared `formatCurrency` utility exists in `@/shared/utils/`,
> use that instead of a local function.

### 4d — Add `useCreatePortalSessionMutation`

Add after the existing mutation hooks:

```tsx
const [createPortalSession, { isLoading: openingPortal }] = useCreatePortalSessionMutation();

async function handleManageBilling() {
  const returnUrl = window.location.href;
  const result = await createPortalSession({ returnUrl });
  if ("data" in result && result.data?.url) {
    window.location.href = result.data.url;
  }
}
```

### 4e — Widen the container

Change `max-w-lg` to `max-w-2xl` on the `<main>` element:

```tsx
<main className="max-w-2xl mx-auto px-4 py-8 space-y-4">
```

### 4f — Remove "Change plan" from the header

The header currently shows "Change plan" as an outline button for `canChangePlan`. Remove that
header button entirely — it will live inside the status card instead.

Keep the "Subscribe / Reactivate" button in the header (for Trialing, GracePeriod, PastDue,
Cancelled states). The header after cleanup:

```tsx
<header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
  <div className="flex items-center gap-2">
    <CreditCard className="h-5 w-5" />
    <span className="font-semibold tracking-tight">Billing</span>
  </div>
  {canSubscribe && (
    <Button size="sm" onClick={() => navigate("/billing/subscribe")} className="gap-1.5">
      <Zap className="h-3.5 w-3.5" />
      {sub.status === "Trialing" || sub.status === "GracePeriod" ? "Subscribe" : "Reactivate"}
    </Button>
  )}
</header>
```

### 4g — Redesign the status card

Replace the entire status `<Card>` block with this redesigned version:

```tsx
<Card>
  <CardContent className="p-5 space-y-4">

    {/* Row 1: Plan badge + Status badge */}
    <div className="flex items-center gap-2 flex-wrap">
      {currentPlan ? (
        <Badge variant="outline" className="text-sm font-medium px-2.5 py-0.5">
          {currentPlan.name}
        </Badge>
      ) : (
        <span className="text-sm text-muted-foreground">No plan selected</span>
      )}
      <span
        className={cn(
          "inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5 text-xs font-medium",
          sub.status === "Active"      && "border-green-500/20 bg-green-500/10 text-green-600 dark:text-green-400",
          sub.status === "Trialing"    && "border-blue-500/20 bg-blue-500/10 text-blue-600 dark:text-blue-400",
          sub.status === "GracePeriod" && "border-amber-500/20 bg-amber-500/10 text-amber-600 dark:text-amber-400",
          sub.status === "PastDue"     && "border-red-500/20 bg-red-500/10 text-red-600 dark:text-red-400",
          sub.status === "Cancelled"   && "border-border bg-muted text-muted-foreground",
        )}
      >
        {cfg.icon}
        {cfg.label}
      </span>
    </div>

    {/* Row 2: Price + renewal date (Active states only) */}
    {sub.status === "Active" && (
      <div className="space-y-1">
        {currentPlan && (
          <p className="text-sm font-medium">
            {formatEur(currentPlan.priceMonthly)}
            <span className="text-muted-foreground font-normal"> / month</span>
          </p>
        )}
        <div className="flex items-center gap-1.5 text-sm text-muted-foreground">
          <Calendar className="h-3.5 w-3.5 shrink-0" />
          {isCashBilled
            ? <span>Active until {formatDate(sub.currentPeriodEnd)}</span>
            : currentPlan
              ? <span>Next charge: {formatEur(currentPlan.priceMonthly)} on {formatDate(sub.currentPeriodEnd)}</span>
              : <span>Renews {formatDate(sub.currentPeriodEnd)}</span>
          }
        </div>
      </div>
    )}

    {/* Trial remaining (Trialing) */}
    {sub.status === "Trialing" && (
      <div className="space-y-1">
        <p className="text-sm">
          Trial ends <span className="font-medium">{formatDate(sub.trialExpiresAt)}</span>
        </p>
        <p className="text-xs text-muted-foreground">
          {daysUntil(sub.trialExpiresAt)} day{daysUntil(sub.trialExpiresAt) !== 1 ? "s" : ""} remaining
        </p>
      </div>
    )}

    {/* Grace period warning (GracePeriod) */}
    {sub.status === "GracePeriod" && (
      <div className="rounded-md border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs text-amber-600 dark:text-amber-400 space-y-0.5">
        <p className="font-medium">Trial expired — your studio is in read-only mode.</p>
        <p>Subscribe before {formatDate(sub.gracePeriodEnd)} to restore full access.</p>
        <p className="text-muted-foreground">
          {daysUntil(sub.gracePeriodEnd)} day{daysUntil(sub.gracePeriodEnd) !== 1 ? "s" : ""} left.
        </p>
      </div>
    )}

    {/* Payment failed warning (PastDue) */}
    {sub.status === "PastDue" && (
      <div className="rounded-md border border-red-500/30 bg-red-500/10 px-3 py-2 text-xs text-red-600 dark:text-red-400">
        <p className="font-medium">Your last payment failed.</p>
        <p>Update your payment method to restore access.</p>
      </div>
    )}

    {/* Cancelled */}
    {sub.status === "Cancelled" && (
      <p className="text-sm text-muted-foreground">
        Your subscription has been cancelled. Reactivate to continue using the platform.
      </p>
    )}

    {/* Actions — Change plan (primary) + Manage billing (secondary) for Active card-billed */}
    {canChangePlan && (
      <div className="flex items-center gap-2 pt-1 flex-wrap">
        <Button
          size="sm"
          className="gap-1.5"
          onClick={() => navigate("/billing/subscribe")}
        >
          <RefreshCw className="h-3.5 w-3.5" />
          Change plan
        </Button>
        <Button
          size="sm"
          variant="outline"
          className="gap-1.5"
          disabled={openingPortal}
          onClick={handleManageBilling}
        >
          {openingPortal
            ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
            : <Settings className="h-3.5 w-3.5" />
          }
          Manage billing
          {!openingPortal && <ExternalLink className="h-3 w-3 opacity-40" />}
        </Button>
      </div>
    )}
  </CardContent>
</Card>
```

The key changes in the status card:
- Status is a pill badge with colored background, not just colored text
- Plan is shown as a `<Badge variant="outline">` — no "Plan:" prefix
- Price is shown (`€29 / month`) for Active states
- "Next charge: €29 on date" for Active card-billed — amount + date together
- "Change plan" button is now primary (filled) and lives inside the card
- "Manage billing" button (secondary, outline) opens Stripe Customer Portal

---

## Part 5 — `BillingPage.test.tsx` — Test Updates

### 5a — One existing test to UPDATE

**Test:** `"shows a loading spinner while data is loading"` — currently checks `getByText("Loading…")`.
After the change to `BillingPageSkeleton`, this text no longer exists. Update:

```ts
// BEFORE
it("shows a loading spinner while data is loading", () => {
  renderPage();
  expect(screen.getByText("Loading…")).toBeInTheDocument();
});

// AFTER
it("shows a skeleton loading state while data is loading", () => {
  renderPage();
  expect(screen.getByLabelText("Loading billing information")).toBeInTheDocument();
});
```

Note: The skeleton `<main>` has `aria-label="Loading billing information"` (added in Part 4b).

### 5b — Add MSW handler for portal session

Add to the MSW server default handlers:

```ts
http.post("http://localhost/api/v1/billing/portal", () =>
  HttpResponse.json({ url: "https://billing.stripe.com/session/test_xxx" }),
),
```

Also add `window.location` mock at the top of the file (after the import block):

```ts
// Portal session redirects use window.location.href — mock it for testing
const assignMock = vi.fn();
Object.defineProperty(window, "location", {
  value: { href: "", assign: assignMock },
  writable: true,
});
```

### 5c — New tests to append

```ts
// ── Plan badge and price display ──────────────────────────────────────────────

it("shows plan name as a badge (not 'Plan: Starter')", async () => {
  renderPage();
  // Plan-1 = "Starter" — should appear as badge text, not in "Plan: Starter" key:value
  expect(await screen.findByText("Starter")).toBeInTheDocument();
  // The raw key:value format should NOT appear
  expect(screen.queryByText(/^plan:/i)).not.toBeInTheDocument();
});

it("shows monthly price for Active subscription", async () => {
  // Default server returns SUB_ACTIVE_CASH with plan-1 (priceMonthly: 29)
  renderPage();
  await screen.findByText("Active");
  // Should show some price representation containing "29"
  const priceEl = screen.getByText(/29/);
  expect(priceEl).toBeInTheDocument();
});

it("shows 'Next charge' with amount for Active card-billed subscription", async () => {
  server.use(
    http.get("http://localhost/api/v1/billing/subscription", () =>
      HttpResponse.json(SUB_ACTIVE_CARD),
    ),
  );
  renderPage();
  await screen.findByText("Active");
  expect(screen.getByText(/next charge/i)).toBeInTheDocument();
});

it("shows 'Active until' (not 'Next charge') for Active cash-billed subscription", async () => {
  // Default: SUB_ACTIVE_CASH
  renderPage();
  await screen.findByText("Active");
  expect(await screen.findByText(/active until/i)).toBeInTheDocument();
  expect(screen.queryByText(/next charge/i)).not.toBeInTheDocument();
});

// ── Status badge visual indicator ─────────────────────────────────────────────

it("renders the Active status as a pill element (not just colored text)", async () => {
  renderPage();
  await screen.findByText("Active");
  // The status pill should be a span (not just a paragraph)
  const activePill = screen.getByText("Active");
  expect(activePill.tagName.toLowerCase()).toBe("span");
});

// ── Change plan button relocation ─────────────────────────────────────────────

it("Change plan button is NOT in the page header for Active card-billed", async () => {
  server.use(
    http.get("http://localhost/api/v1/billing/subscription", () =>
      HttpResponse.json(SUB_ACTIVE_CARD),
    ),
  );
  renderPage();
  const changePlanBtn = await screen.findByRole("button", { name: /change plan/i });
  // The button should now be inside <main>, not inside <header>
  const header = document.querySelector("header");
  expect(header).not.toContainElement(changePlanBtn);
});

// ── Manage billing (Stripe Customer Portal) ───────────────────────────────────

it("shows Manage billing button for Active card-billed subscription", async () => {
  server.use(
    http.get("http://localhost/api/v1/billing/subscription", () =>
      HttpResponse.json(SUB_ACTIVE_CARD),
    ),
  );
  renderPage();
  expect(await screen.findByRole("button", { name: /manage billing/i })).toBeInTheDocument();
});

it("does NOT show Manage billing button for Active cash-billed subscription", async () => {
  // Default: SUB_ACTIVE_CASH
  renderPage();
  await screen.findByText("Active");
  expect(screen.queryByRole("button", { name: /manage billing/i })).not.toBeInTheDocument();
});

it("does NOT show Manage billing button when subscription is Trialing", async () => {
  server.use(
    http.get("http://localhost/api/v1/billing/subscription", () =>
      HttpResponse.json(SUB_TRIALING),
    ),
  );
  renderPage();
  await screen.findByText("Trial");
  expect(screen.queryByRole("button", { name: /manage billing/i })).not.toBeInTheDocument();
});

it("does NOT show Manage billing button when subscription is Cancelled", async () => {
  server.use(
    http.get("http://localhost/api/v1/billing/subscription", () =>
      HttpResponse.json(SUB_CANCELLED),
    ),
  );
  renderPage();
  await screen.findByText("Cancelled");
  expect(screen.queryByRole("button", { name: /manage billing/i })).not.toBeInTheDocument();
});

it("clicking Manage billing calls the portal mutation and redirects", async () => {
  const portalSpy = vi.fn();
  server.use(
    http.get("http://localhost/api/v1/billing/subscription", () =>
      HttpResponse.json(SUB_ACTIVE_CARD),
    ),
    http.post("http://localhost/api/v1/billing/portal", async ({ request }) => {
      const body = await request.json() as { returnUrl: string };
      portalSpy(body);
      return HttpResponse.json({ url: "https://billing.stripe.com/session/test_xyz" });
    }),
  );

  const user = userEvent.setup();
  renderPage();
  await screen.findByRole("button", { name: /manage billing/i });

  await user.click(screen.getByRole("button", { name: /manage billing/i }));

  await waitFor(() => expect(portalSpy).toHaveBeenCalledOnce());
  expect(window.location.href).toBe("https://billing.stripe.com/session/test_xyz");
});
```

---

## Part 6 — Verify

```bash
# TypeScript — frontend
pnpm tsc --noEmit

# Billing tests — must all pass (1 updated + 10 new = 50 total)
pnpm test src/features/billing/BillingPage --run

# Smoke-check SubscribePage is not broken
pnpm test src/features/billing --run

# Full frontend suite
pnpm test --run

# Backend build
dotnet build

# Backend tests — CreateBillingPortalHandlerTests must pass
dotnet test --filter "Category=Billing|FullyQualifiedName~CreateBillingPortal"
```

---

## Architecture Decisions

**Why Stripe Customer Portal instead of custom cancel/payment-method UI?**
The Customer Portal is Stripe's hosted solution for self-service billing management. One backend
endpoint (`POST /billing/portal`) replaces three separate custom UIs (cancel, payment method,
invoice download). The user is redirected to a Stripe-hosted page, makes changes, and returns to
`/billing`. The subscription webhook updates the local `Subscription` record automatically.

**Why move "Change plan" into the card?**
The button acts on the plan — it should be visually adjacent to the content it modifies. A button
in the page header that acts on mid-page content is a UX anti-pattern (high click distance,
low affordance).

**Why `primary` style for "Change plan"?**
It is the primary self-serve revenue action. On the Active card-billed billing page, it should be
the most prominent CTA. "Manage billing" (portal) is secondary.

---

## Constraints (from CLAUDE.md)

- Do NOT add new npm or NuGet packages.
- No useEffect for data fetching — `useMemo` for derived values; RTK Query for all fetches.
- TypeScript strict mode — no `any`, explicit types everywhere.
- No default exports on components.
- No business logic in the API endpoint — endpoint calls MediatR only.
- Every DB query goes through global query filters — the `Subscriptions` query is tenant-scoped
  via `currentUser.TenantId`.
- `OwnerOnly` policy on the portal endpoint — artists and clients cannot create billing portal
  sessions.
- No PII in logs — only `tenant_id`, `user_id`, `request_id`.
