# Overnight Prompt — Studios List UI/UX Overhaul (2026-06-17)

> **Scope:** UI/UX audit of `IssuerStudioListPage.tsx` — layout bugs, copy fixes,
> action button polish, meta line corrections, loading states, empty state, plan
> filter, skeleton loader, and a new studio detail drill-down page backed by a new
> `GET /api/v1/studios/:id` backend endpoint.
>
> **Frontend files primary.** One new backend endpoint.
> No new npm or NuGet packages. No database migrations.
> Commit after each numbered task.

---

## 0. Mandatory Reading (Do This First)

```
CLAUDE.md
docs/claude/frontend.md
docs/claude/backend.md
docs/claude/conventions.md
docs/claude/architecture.md
```

Then read these source files:

```
frontend/src/features/platform/components/IssuerStudioListPage.tsx
frontend/src/features/platform/__tests__/IssuerStudioListPage.test.tsx
frontend/src/features/studios/studiosApi.ts
frontend/src/app/router.tsx
frontend/src/features/platform/index.ts
Pena_e_Arte.Contracts/Responses/StudioResponse.cs
Pena_e_Arte.Application/Studios/Queries/GetStudiosQuery.cs
Pena_e_Arte.API/Endpoints/StudioEndpoints.cs
```

---

## 1. Copy & Status Label Fixes (Frontend)

**File:** `frontend/src/features/platform/components/IssuerStudioListPage.tsx`

### 1a. Rename "Trialing" → "In Trial"

In `STATUS_LABELS`:

```typescript
const STATUS_LABELS: Record<string, string> = {
  Active:         "Active",
  Trialing:       "In Trial",    // was "Trialing"
  PastDue:        "Past Due",
  GracePeriod:    "Grace Period",
  Cancelled:      "Cancelled",
  NoSubscription: "No Subscription",
  Suspended:      "Suspended",
};
```

This affects both the badge text and the filter dropdown `<option>` labels.
Also update the filter dropdown `<option>` for `Trialing` to read "In Trial":

```tsx
<option key={s} value={s}>{STATUS_LABELS[s]}</option>
```

This already uses `STATUS_LABELS`, so the option text changes automatically.

### 1b. Fix "No plan" when status is Trialing

The meta line currently reads `{sub?.planName ?? "No plan"}`. When a studio
is in `Trialing` status, `planName` is `null` and "No plan" is rendered — but
they ARE on a trial plan, so the text is contradictory.

Replace the meta line `p` element:

```tsx
const planDisplay = (() => {
  if (subStatus === "Trialing") return "In Trial";
  if (subStatus === "NoSubscription") return "No subscription";
  return sub?.planName ?? "—";
})();
```

Then in the JSX:

```tsx
<p className="text-xs text-muted-foreground">
  {studio.city}
  {" · "}Registered {fmt(studio.createdAt)}
  {" · "}{planDisplay}
  {" · "}{periodText}
</p>
```

### 1c. Fix contradictory periodText for expired trials

The current `periodText` logic for `GracePeriod` studios reads "Trial expired"
even though they are in a grace period. Replace the whole `periodText` block:

```typescript
const trialDate    = sub?.trialExpiresAt ?? studio.trialExpiresAt;
const trialExpired = new Date(trialDate) < new Date();

const periodText = (() => {
  if (sub?.status === "Active" && sub?.currentPeriodEnd) {
    return `Renews: ${fmt(sub.currentPeriodEnd)}`;
  }
  if (sub?.status === "GracePeriod") {
    const periodEnd = sub.currentPeriodEnd;
    return `Grace ends: ${fmt(periodEnd)}`;
  }
  if (sub?.status === "PastDue" && sub?.currentPeriodEnd) {
    return `Overdue since: ${fmt(sub.currentPeriodEnd)}`;
  }
  if (trialExpired) return `Trial expired: ${fmt(trialDate)}`;
  return `Expires: ${fmt(trialDate)}`;
})();
```

### 1d. Rename "Cancel sub" → "Cancel Subscription" everywhere

Find every occurrence of `"Cancel sub"` in `IssuerStudioListPage.tsx` (there are
two — the button label and the confirmation text) and replace with
`"Cancel Subscription"`.

Also update the confirmation prompt text from:
```tsx
<span className="text-xs text-destructive font-medium">Cancel this subscription?</span>
```
to:
```tsx
<span className="text-xs text-destructive font-medium">Cancel subscription permanently?</span>
```

### 1e. Fix "Extend trial" label for expired trials

The button currently reads "Extend trial" regardless of whether the trial has
already expired. When `trialExpired` is true, the button should say
"Grant extension":

```tsx
{!anyExpanded && canExtendTrial && (
  <Button size="sm" variant="outline" className="h-7 text-xs gap-1"
    onClick={() => setExtending(true)}>
    <Clock className="h-3.5 w-3.5" />
    {trialExpired ? "Grant extension" : "Extend Trial (+7 days)"}
  </Button>
)}
```

Note: the `+7 days` suffix matches the default `days` state value. When the user
opens the form and changes the value, the button label is already replaced by the
inline form so it never shows a stale number.

Run `pnpm lint` — must pass.

**Commit:** `fix(studios-list): copy — In Trial, No plan, Cancel Subscription, period labels`

---

## 2. Icon Consistency & Button Polish (Frontend)

**File:** `frontend/src/features/platform/components/IssuerStudioListPage.tsx`

Add to the `lucide-react` import:

```tsx
import {
  Banknote,
  Building2,
  Clock,
  ExternalLink,
  Loader2,
  PauseCircle,
  PlayCircle,
  Search,
  XCircle,
} from "lucide-react";
```

(`Clock` → Extend trial / Grant extension, `XCircle` → Cancel Subscription,
`ExternalLink` → View detail link added in Task 4)

### 2a. Add icon to "Cancel Subscription" button

```tsx
{!anyExpanded && canCancel && (
  <Button
    size="sm" variant="outline"
    className="h-7 text-xs gap-1 text-destructive border-destructive/40 hover:bg-destructive/10 hover:text-destructive"
    onClick={() => setConfirming(true)}>
    <XCircle className="h-3.5 w-3.5" />
    Cancel Subscription
  </Button>
)}
```

### 2b. Visual hierarchy — primary vs secondary vs destructive

The "Activate" button is the primary action but looks identical to everything
else. Fix visual weight:

```tsx
{/* Activate — primary action (filled) */}
{!anyExpanded && canActivate && (
  <Button size="sm" className="h-7 text-xs gap-1"
    onClick={() => setActivating(true)}>
    <Banknote className="h-3.5 w-3.5" />
    Activate
  </Button>
)}
```

(Remove `variant="outline"` from Activate — it becomes the default filled button.)

Suspend/Reactivate stay as `variant="ghost"` — already correct.

### 2c. Fix button ordering — destructive last

The audit found that "Cancel Subscription" appears before "Suspend" — wrong order.
A destructive irreversible action must always appear last.

New button rendering order in the `!anyExpanded` block:

1. `canExtendTrial` → "Extend Trial (+7 days)" / "Grant extension" (outline, Clock)
2. `canActivate` → "Activate" (filled, Banknote) — primary
3. Platform Suspend/Reactivate (ghost, PauseCircle/PlayCircle)
4. `canCancel` → "Cancel Subscription" (outline/destructive, XCircle) — LAST

Full updated button group:

```tsx
<div className="flex items-center gap-1.5 shrink-0 flex-wrap justify-end">
  {/* 1. Extend trial */}
  {!anyExpanded && canExtendTrial && (
    <Button size="sm" variant="outline" className="h-7 text-xs gap-1"
      onClick={() => setExtending(true)}>
      <Clock className="h-3.5 w-3.5" />
      {trialExpired ? "Grant extension" : "Extend Trial (+7 days)"}
    </Button>
  )}

  {/* 2. Activate (primary — filled) */}
  {!anyExpanded && canActivate && (
    <Button size="sm" className="h-7 text-xs gap-1"
      onClick={() => setActivating(true)}>
      <Banknote className="h-3.5 w-3.5" />
      Activate
    </Button>
  )}

  {/* 3. Suspend / Reactivate (ghost) — platform action */}
  {confirmPlatform ? (
    <>
      <span className="text-xs text-muted-foreground">
        {confirmPlatform === "suspend" ? "Suspend?" : "Reactivate?"}
      </span>
      <Button
        size="sm"
        variant={confirmPlatform === "suspend" ? "destructive" : "default"}
        className="h-7 px-2 text-xs"
        disabled={suspending || unsuspending}
        onClick={executePlatform}
      >
        {(suspending || unsuspending)
          ? <Loader2 className="h-3 w-3 animate-spin" />
          : "Yes"}
      </Button>
      <Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
        onClick={() => setConfirmPlatform(null)}>
        No
      </Button>
    </>
  ) : (
    !anyExpanded && (
      <Button
        size="sm" variant="ghost" className="h-7 px-2 text-xs gap-1"
        onClick={() => setConfirmPlatform(isSuspended ? "unsuspend" : "suspend")}
      >
        {isSuspended
          ? <><PlayCircle className="h-3.5 w-3.5" /> Reactivate</>
          : <><PauseCircle className="h-3.5 w-3.5" /> Suspend</>}
      </Button>
    )
  )}

  {/* 4. Cancel Subscription (destructive outline — LAST) */}
  {!anyExpanded && canCancel && (
    <Button
      size="sm" variant="outline"
      className="h-7 text-xs gap-1 text-destructive border-destructive/40 hover:bg-destructive/10 hover:text-destructive"
      onClick={() => setConfirming(true)}>
      <XCircle className="h-3.5 w-3.5" />
      Cancel Subscription
    </Button>
  )}
</div>
```

Run `pnpm lint` — must pass.

**Commit:** `fix(studios-list): icon consistency, button ordering, primary action weight`

---

## 3. Layout Bug Fix — Badge Wrapping (Frontend)

**File:** `frontend/src/features/platform/components/IssuerStudioListPage.tsx`

The studio name + slug + badge header line has `flex-wrap` enabled, which causes
the badge to drop to a second line when the slug is long (e.g.
"pena-e-arte-lisboa2"). This breaks row height consistency.

Fix: switch the header div to `flex-nowrap` and truncate the slug with ellipsis.

Replace:

```tsx
<div className="flex items-center gap-2 flex-wrap">
  <span className="font-medium text-sm">{studio.name}</span>
  <span className="text-xs text-muted-foreground font-mono">{studio.slug}</span>
  <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium ${STATUS_CLASSES[badgeStatus]}`}>
    {STATUS_LABELS[badgeStatus]}
  </span>
</div>
```

With:

```tsx
<div className="flex items-center gap-2 flex-nowrap min-w-0">
  <span className="font-medium text-sm shrink-0">{studio.name}</span>
  <span className="text-xs text-muted-foreground font-mono truncate max-w-[180px]"
        title={studio.slug}>
    {studio.slug}
  </span>
  <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium shrink-0 ${STATUS_CLASSES[badgeStatus]}`}>
    {STATUS_LABELS[badgeStatus]}
  </span>
</div>
```

The `title={studio.slug}` tooltip shows the full slug on hover for truncated slugs.
`shrink-0` on the badge prevents it from being squeezed.
`max-w-[180px]` caps slug width — adjust if needed based on typical slug lengths.

Run `pnpm lint` — must pass.

**Commit:** `fix(studios-list): badge never wraps — flex-nowrap + slug truncate`

---

## 4. Skeleton Loading State (Frontend)

**File:** `frontend/src/features/platform/components/IssuerStudioListPage.tsx`

The current loading state shows a centered spinner. Replace with skeleton cards
that match the real row shape so there's no layout flash.

Add these components above `StudioRow`:

```tsx
import { Skeleton } from "@/shared/components/ui/skeleton";

function StudioRowSkeleton() {
  return (
    <Card>
      <CardContent className="p-4 space-y-2">
        <div className="flex items-start justify-between gap-4">
          <div className="space-y-1.5 flex-1">
            <div className="flex items-center gap-2">
              <Skeleton className="h-4 w-32" />
              <Skeleton className="h-3 w-28" />
              <Skeleton className="h-5 w-16 rounded-full" />
            </div>
            <Skeleton className="h-3 w-64" />
          </div>
          <div className="flex items-center gap-1.5">
            <Skeleton className="h-7 w-20" />
            <Skeleton className="h-7 w-16" />
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
```

In `IssuerStudioListPage`, replace the loading block:

```tsx
{/* Before: */}
{isLoading && (
  <div className="flex items-center justify-center py-16 gap-2 text-muted-foreground">
    <Loader2 className="h-5 w-5 animate-spin" />
    <span className="text-sm">Loading…</span>
  </div>
)}

{/* After: */}
{isLoading && (
  <div className="space-y-3">
    {[1, 2, 3, 4, 5].map((i) => <StudioRowSkeleton key={i} />)}
  </div>
)}
```

Remove the `Loader2` import from the top of `IssuerStudioListPage` if it is no
longer used in the page-level loading block. It IS still used inside `StudioRow`
for button loading states — check before removing.

Run `pnpm lint` — must pass.

**Commit:** `feat(studios-list): skeleton loading state replaces spinner`

---

## 5. Plan Filter Dropdown (Frontend)

**File:** `frontend/src/features/platform/components/IssuerStudioListPage.tsx`

Add a plan filter alongside the status filter. The available plan names come from
the already-fetched `plans` data (currently only fetched inside `StudioRow` — move
this query to the page level).

### 5a. Move `useGetIssuerPlansQuery` to page level

Remove `const { data: plans = [] } = useGetIssuerPlansQuery();` from inside
`StudioRow`. Instead, pass `plans` as a prop:

```tsx
interface StudioRowProps {
  studio: StudioResponse;
  sub:    PlatformSubscriptionResponse | undefined;
  plans:  PlanResponse[];
}

function StudioRow({ studio, sub, plans }: StudioRowProps) {
  // ... remove the useGetIssuerPlansQuery call
}
```

In `IssuerStudioListPage`, add at the top:

```tsx
import type { PlanResponse } from "@/features/billing/billing.types";
// ... existing imports
const { data: plans = [] } = useGetIssuerPlansQuery();
```

Pass `plans` to each `StudioRow`:
```tsx
<StudioRow key={s.id} studio={s} sub={subMap.get(s.id)} plans={plans} />
```

### 5b. Add plan filter state and UI

```tsx
const [planFilter, setPlanFilter] = useState("all");
```

Plan names are derived from `plans` (already fetched). Add a second filter `<select>`
in the search bar area, after the status filter:

```tsx
<select
  value={planFilter}
  onChange={(e) => setPlanFilter(e.target.value)}
  className="h-8 rounded-md border border-input bg-background px-2 text-xs"
>
  <option value="all">All plans</option>
  {plans.map((p) => (
    <option key={p.id} value={p.name}>{p.name}</option>
  ))}
  <option value="none">No plan</option>
</select>
```

### 5c. Apply plan filter in `filtered` useMemo

```typescript
const filtered = useMemo(() => {
  if (!studios) return [];
  const q = search.trim().toLowerCase();
  return studios.filter((s) => {
    const sub             = subMap.get(s.id);
    const subStatus       = sub?.status ?? "NoSubscription";
    const effectiveStatus = !s.isActive ? "Suspended" : subStatus;

    const matchesSearch = !q ||
      s.name.toLowerCase().includes(q) ||
      s.slug.toLowerCase().includes(q);
    const matchesStatus =
      statusFilter === "all" || effectiveStatus === statusFilter;
    const matchesPlan = (() => {
      if (planFilter === "all") return true;
      if (planFilter === "none") return sub?.planName == null;
      return sub?.planName === planFilter;
    })();

    return matchesSearch && matchesStatus && matchesPlan;
  });
}, [studios, subMap, search, statusFilter, planFilter]);
```

### 5d. Update the search bar container

Change the search bar container from `flex gap-2` to accommodate three controls:

```tsx
<div className="max-w-3xl mx-auto px-4 pt-4 flex gap-2 flex-wrap">
```

(`flex-wrap` here is intentional — the controls should stack on small screens.
This is the filter bar, not a card row, so wrapping is fine.)

Run `pnpm lint` — must pass.

**Commit:** `feat(studios-list): plan filter dropdown, move plans query to page level`

---

## 6. Backend — `GET /api/v1/studios/:id` (IssuerOnly)

The studio detail page (Task 7) needs to fetch a single studio by ID. There is
currently no such endpoint. Add it.

### 6a. Add `GetStudioByIdQuery`

**New file:** `Pena_e_Arte.Application/Studios/Queries/GetStudioByIdQuery.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;

namespace Pena_e_Arte.Application.Studios.Queries;

public record GetStudioByIdQuery(Guid StudioId) : IRequest<StudioResponse>;

public class GetStudioByIdHandler(IAppDbContext db)
    : IRequestHandler<GetStudioByIdQuery, StudioResponse>
{
    public async Task<StudioResponse> Handle(GetStudioByIdQuery query, CancellationToken ct)
    {
        // IssuerOnly endpoint — IgnoreQueryFilters approved: usage #4 (cross-tenant read).
        // See architecture.md Approved Usages table.
        StudioResponse? studio = await db.Studios
            .IgnoreQueryFilters()
            .Select(s => new StudioResponse(
                s.Id, s.Name, s.Slug, s.City,
                s.Latitude, s.Longitude,
                s.ShowPlatformBranding,
                AllowBrandingRemoval: false,
                s.TrialExpiresAt, s.CreatedAt, s.IsActive))
            .FirstOrDefaultAsync(s => s.Id == query.StudioId, ct);

        if (studio is null)
            throw new NotFoundException($"Studio {query.StudioId} not found.");

        return studio;
    }
}
```

> Check `architecture.md` Approved Usages table. `GetStudiosHandler` is already in
> it for `IssuerOnly`. Add a row for `GetStudioByIdHandler` using the same
> justification (issuer cross-tenant read, approved usage #4).

### 6b. Add endpoint

**File:** `Pena_e_Arte.API/Endpoints/StudioEndpoints.cs`

Add after the `GetStudios` registration:

```csharp
// Issuer: get single studio by id
group.MapGet("{id:guid}", GetStudioById).RequireAuthorization("IssuerOnly");
```

Add the handler method:

```csharp
private static async Task<IResult> GetStudioById(
    Guid              id,
    ISender           mediator,
    CancellationToken ct)
{
    StudioResponse result = await mediator.Send(new GetStudioByIdQuery(id), ct);
    return Results.Ok(result);
}
```

> **Route ordering note:** `GET /{id:guid}` must be registered AFTER
> `GET /me` and `GET /map`. In `MapStudioEndpoints`, ensure `/me` and `/map`
> are mapped first (they already are, based on the existing file). The GUID
> constraint ensures the catch-all `{id:guid}` only matches valid GUIDs and
> doesn't shadow the named sub-routes.

### 6c. Add to `architecture.md` IgnoreQueryFilters table

Find the IgnoreQueryFilters Approved Usages table in `docs/claude/architecture.md`.
Append a row:

```
| 5 | GetStudioByIdHandler | IssuerOnly | Cross-tenant single-studio read for admin detail page |
```

Run `dotnet build` — must succeed.

**Commit:** `feat(api): GET /api/v1/studios/:id IssuerOnly endpoint`

---

## 7. Frontend — `IssuerStudioDetailPage` (New File)

The studio detail drill-down page. The issuer navigates here by clicking "View →"
from a row in `IssuerStudioListPage`.

**New file:** `frontend/src/features/platform/components/IssuerStudioDetailPage.tsx`

The page:
- Fetches a single studio by `studioId` param from `studiosApi`
- Fetches the subscription record from `useGetPlatformSubscriptionsQuery` and
  finds the matching one by `studioId`
- Renders: name, slug, status badge, city, registration date, plan, period dates,
  `isActive` flag, trial expiry
- Renders the same action buttons as the list row (Extend trial, Activate, Suspend,
  Cancel) — reuse `StudioRow` or extract into a shared component

### 7a. Add `getStudioById` to `studiosApi.ts`

**File:** `frontend/src/features/studios/studiosApi.ts`

Add a new query:

```typescript
getStudioById: builder.query<StudioResponse, string>({
  query: (id) => `studios/${id}`,
  providesTags: ["Studio"],
}),
```

Export the hook:

```typescript
export const {
  // ... existing exports
  useGetStudioByIdQuery,
} = studiosApi;
```

### 7b. Create `IssuerStudioDetailPage.tsx`

```tsx
import { useParams, Link } from "react-router-dom";
import { ArrowLeft, Building2 } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useGetStudioByIdQuery } from "@/features/studios/studiosApi";
import {
  useGetPlatformSubscriptionsQuery,
  useExtendTrialMutation,
  useActivateSubscriptionManuallyMutation,
  useCancelSubscriptionMutation,
} from "@/features/platform/platformApi";
import { useGetIssuerPlansQuery } from "@/features/billing/billingApi";
import { StudioRow } from "@/features/platform/components/IssuerStudioListPage";

// NOTE: If StudioRow is not yet exported from IssuerStudioListPage, add
// export { StudioRow } to IssuerStudioListPage.tsx — or inline the action
// buttons here. Prefer the inline approach to avoid coupling.
```

> **Do not export `StudioRow` from `IssuerStudioListPage`.** Exporting an
> internal component creates coupling. Instead, write the detail page's action
> section directly, following the same pattern as `StudioRow`. The detail page
> is a different layout context and the extra few lines are worthwhile.

Full implementation:

```tsx
import { useState, useMemo } from "react";
import { useParams, Link } from "react-router-dom";
import {
  ArrowLeft,
  Banknote,
  Building2,
  Clock,
  ExternalLink,
  Loader2,
  PauseCircle,
  PlayCircle,
  XCircle,
} from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useGetStudioByIdQuery } from "@/features/studios/studiosApi";
import {
  useGetPlatformSubscriptionsQuery,
  useExtendTrialMutation,
  useActivateSubscriptionManuallyMutation,
  useCancelSubscriptionMutation,
} from "@/features/platform/platformApi";
import {
  useSuspendStudioMutation,
  useUnsuspendStudioMutation,
} from "@/features/studios/studiosApi";
import { useGetIssuerPlansQuery } from "@/features/billing/billingApi";
import type { PlatformSubscriptionResponse } from "@/features/platform/platform.types";

const STATUS_CLASSES: Record<string, string> = {
  Active:         "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300",
  Trialing:       "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300",
  PastDue:        "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-300",
  GracePeriod:    "bg-orange-100 text-orange-700 dark:bg-orange-900/30 dark:text-orange-300",
  Cancelled:      "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300",
  NoSubscription: "bg-muted text-muted-foreground",
  Suspended:      "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300",
};

const STATUS_LABELS: Record<string, string> = {
  Active:         "Active",
  Trialing:       "In Trial",
  PastDue:        "Past Due",
  GracePeriod:    "Grace Period",
  Cancelled:      "Cancelled",
  NoSubscription: "No Subscription",
  Suspended:      "Suspended",
};

const CASH_ACTIVATABLE = new Set(["NoSubscription", "PastDue", "GracePeriod", "Cancelled"]);
const CANCELLABLE      = new Set(["Active", "PastDue", "Trialing", "GracePeriod"]);

function fmt(date: string | Date) {
  return new Date(date).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

export function IssuerStudioDetailPage() {
  const { studioId } = useParams<{ studioId: string }>();

  const { data: studio, isLoading: studioLoading, isError } =
    useGetStudioByIdQuery(studioId!, { skip: !studioId });
  const { data: subscriptions } =
    useGetPlatformSubscriptionsQuery(undefined, { refetchOnMountOrArgChange: true });
  const { data: plans = [] } = useGetIssuerPlansQuery();

  const sub: PlatformSubscriptionResponse | undefined = useMemo(
    () => subscriptions?.find((s) => s.studioId === studioId),
    [subscriptions, studioId],
  );

  const isSuspended = studio ? !studio.isActive : false;
  const subStatus   = sub?.status ?? "NoSubscription";
  const badgeStatus = isSuspended ? "Suspended" : subStatus;

  // Platform actions
  const [confirmPlatform, setConfirmPlatform] = useState<"suspend" | "unsuspend" | null>(null);
  const [suspend,   { isLoading: suspending   }] = useSuspendStudioMutation();
  const [unsuspend, { isLoading: unsuspending }] = useUnsuspendStudioMutation();

  // Subscription actions
  const [extending,  setExtending]  = useState(false);
  const [activating, setActivating] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [days,       setDays]       = useState("7");
  const [cashPlanId, setCashPlanId] = useState("");
  const [cashNote,   setCashNote]   = useState("");

  const [extendTrial,      { isLoading: extending_  }] = useExtendTrialMutation();
  const [activateManually, { isLoading: activating_ }] = useActivateSubscriptionManuallyMutation();
  const [cancelSub,        { isLoading: cancelling_ }] = useCancelSubscriptionMutation();

  const canExtendTrial = subStatus !== "Active";
  const canActivate    = CASH_ACTIVATABLE.has(subStatus);
  const canCancel      = CANCELLABLE.has(subStatus);
  const anyExpanded    = extending || activating || confirming || confirmPlatform !== null;

  const trialDate    = sub?.trialExpiresAt ?? studio?.trialExpiresAt ?? "";
  const trialExpired = trialDate ? new Date(trialDate) < new Date() : false;

  async function executePlatform() {
    if (!studioId) return;
    try {
      if (confirmPlatform === "suspend")   await suspend(studioId).unwrap();
      if (confirmPlatform === "unsuspend") await unsuspend(studioId).unwrap();
    } catch { /* optimistic update rolled back */ }
    finally { setConfirmPlatform(null); }
  }

  async function handleExtend() {
    if (!studioId) return;
    const d = parseInt(days, 10);
    if (isNaN(d) || d < 1) return;
    await extendTrial({ studioId, additionalDays: d }).unwrap();
    setExtending(false);
  }

  async function handleActivate() {
    if (!studioId || !cashPlanId) return;
    await activateManually({ studioId, planId: cashPlanId, note: cashNote || undefined }).unwrap();
    setActivating(false);
    setCashPlanId("");
    setCashNote("");
  }

  async function handleCancel() {
    if (!studioId) return;
    await cancelSub(studioId).unwrap();
    setConfirming(false);
  }

  if (studioLoading) {
    return (
      <div className="min-h-screen bg-background">
        <div className="max-w-3xl mx-auto px-4 py-8 space-y-4">
          <Skeleton className="h-8 w-48" />
          <Skeleton className="h-40 w-full" />
          <Skeleton className="h-24 w-full" />
        </div>
      </div>
    );
  }

  if (isError || !studio) {
    return (
      <div className="min-h-screen bg-background">
        <div className="max-w-3xl mx-auto px-4 py-16 text-center">
          <p className="text-sm text-destructive">Studio not found.</p>
          <Link to="/platform/studios" className="text-sm text-primary hover:underline mt-2 inline-block">
            ← Back to Studios
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Link
          to="/platform/studios"
          className="flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground transition-colors"
        >
          <ArrowLeft className="h-4 w-4" />
          Studios
        </Link>
        <span className="text-muted-foreground">/</span>
        <Building2 className="h-4 w-4" />
        <span className="font-semibold tracking-tight text-sm">{studio.name}</span>
      </header>

      <main className="max-w-3xl mx-auto px-4 py-6 space-y-4">

        {/* ── Studio Info Card ──────────────────────────────────────── */}
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm flex items-center gap-2">
              <span>{studio.name}</span>
              <span
                className={`text-xs px-1.5 py-0.5 rounded-full font-medium ${STATUS_CLASSES[badgeStatus]}`}
              >
                {STATUS_LABELS[badgeStatus]}
              </span>
            </CardTitle>
          </CardHeader>
          <CardContent className="pt-0 space-y-1.5">
            <div className="grid grid-cols-2 gap-x-6 gap-y-1.5 text-xs">
              <div>
                <span className="text-muted-foreground">Slug</span>
                <p className="font-mono">{studio.slug}</p>
              </div>
              <div>
                <span className="text-muted-foreground">City</span>
                <p>{studio.city}</p>
              </div>
              <div>
                <span className="text-muted-foreground">Registered</span>
                <p>{fmt(studio.createdAt)}</p>
              </div>
              <div>
                <span className="text-muted-foreground">Platform branding</span>
                <p>{studio.showPlatformBranding ? "Shown" : "Hidden"}</p>
              </div>
              <div>
                <span className="text-muted-foreground">Plan</span>
                <p>
                  {subStatus === "Trialing"
                    ? "In Trial"
                    : subStatus === "NoSubscription"
                    ? "None"
                    : (sub?.planName ?? "—")}
                </p>
              </div>
              <div>
                <span className="text-muted-foreground">Subscription status</span>
                <p>{STATUS_LABELS[badgeStatus]}</p>
              </div>
              {sub?.currentPeriodEnd && sub.status === "Active" && (
                <div>
                  <span className="text-muted-foreground">Renews</span>
                  <p>{fmt(sub.currentPeriodEnd)}</p>
                </div>
              )}
              {trialDate && (
                <div>
                  <span className="text-muted-foreground">Trial expiry</span>
                  <p className={trialExpired ? "text-destructive" : ""}>
                    {fmt(trialDate)}
                    {trialExpired ? " (expired)" : ""}
                  </p>
                </div>
              )}
            </div>

            {/* Public portfolio link */}
            <a
              href={`/s/${studio.slug}`}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-1 text-xs text-primary hover:underline mt-2"
            >
              <ExternalLink className="h-3 w-3" />
              View public portfolio
            </a>
          </CardContent>
        </Card>

        {/* ── Actions Card ──────────────────────────────────────────── */}
        <Card>
          <CardHeader className="pb-2">
            <CardTitle className="text-sm">Actions</CardTitle>
          </CardHeader>
          <CardContent className="pt-0 space-y-3">
            <div className="flex flex-wrap gap-2">
              {/* 1. Extend trial */}
              {!anyExpanded && canExtendTrial && (
                <Button size="sm" variant="outline" className="h-8 text-xs gap-1"
                  onClick={() => setExtending(true)}>
                  <Clock className="h-3.5 w-3.5" />
                  {trialExpired ? "Grant extension" : "Extend Trial (+7 days)"}
                </Button>
              )}

              {/* 2. Activate */}
              {!anyExpanded && canActivate && (
                <Button size="sm" className="h-8 text-xs gap-1"
                  onClick={() => setActivating(true)}>
                  <Banknote className="h-3.5 w-3.5" />
                  Activate
                </Button>
              )}

              {/* 3. Suspend / Reactivate */}
              {confirmPlatform ? (
                <div className="flex items-center gap-2">
                  <span className="text-xs text-muted-foreground">
                    {confirmPlatform === "suspend" ? "Suspend this studio?" : "Reactivate this studio?"}
                  </span>
                  <Button
                    size="sm"
                    variant={confirmPlatform === "suspend" ? "destructive" : "default"}
                    className="h-8 px-3 text-xs"
                    disabled={suspending || unsuspending}
                    onClick={executePlatform}
                  >
                    {(suspending || unsuspending)
                      ? <Loader2 className="h-3 w-3 animate-spin" />
                      : "Confirm"}
                  </Button>
                  <Button size="sm" variant="ghost" className="h-8 px-3 text-xs"
                    onClick={() => setConfirmPlatform(null)}>
                    Cancel
                  </Button>
                </div>
              ) : (
                !anyExpanded && (
                  <Button
                    size="sm" variant="ghost" className="h-8 text-xs gap-1"
                    onClick={() => setConfirmPlatform(isSuspended ? "unsuspend" : "suspend")}
                  >
                    {isSuspended
                      ? <><PlayCircle className="h-3.5 w-3.5" /> Reactivate</>
                      : <><PauseCircle className="h-3.5 w-3.5" /> Suspend</>}
                  </Button>
                )
              )}

              {/* 4. Cancel Subscription (LAST) */}
              {!anyExpanded && canCancel && (
                <Button
                  size="sm" variant="outline"
                  className="h-8 text-xs gap-1 text-destructive border-destructive/40 hover:bg-destructive/10 hover:text-destructive"
                  onClick={() => setConfirming(true)}>
                  <XCircle className="h-3.5 w-3.5" />
                  Cancel Subscription
                </Button>
              )}
            </div>

            {/* Extend trial form */}
            {extending && (
              <div className="flex items-center gap-2 pt-2 border-t">
                <span className="text-xs text-muted-foreground">
                  {trialExpired ? "Grant extension of" : "Extend trial by"}
                </span>
                <Input
                  type="number" min="1" max="90"
                  value={days} onChange={(e) => setDays(e.target.value)}
                  className="h-7 w-20 text-xs"
                />
                <span className="text-xs text-muted-foreground">days</span>
                <Button size="sm" className="h-7 px-2 text-xs" disabled={extending_} onClick={handleExtend}>
                  {extending_ ? <Loader2 className="h-3 w-3 animate-spin" /> : "Confirm"}
                </Button>
                <Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
                  onClick={() => setExtending(false)}>Cancel</Button>
              </div>
            )}

            {/* Activate (cash) form */}
            {activating && (
              <div className="pt-2 space-y-2 border-t">
                <p className="text-xs font-medium text-muted-foreground">Activate — Cash Payment</p>
                <div className="space-y-1">
                  <Label htmlFor="detail-plan" className="text-xs">Plan</Label>
                  <select
                    id="detail-plan"
                    value={cashPlanId}
                    onChange={(e) => setCashPlanId(e.target.value)}
                    className="h-8 w-full rounded-md border border-input bg-background px-2 text-xs"
                  >
                    <option value="">Select a plan…</option>
                    {plans.map((p) => (
                      <option key={p.id} value={p.id}>{p.name}</option>
                    ))}
                  </select>
                </div>
                <div className="space-y-1">
                  <Label htmlFor="detail-note" className="text-xs">Note (optional)</Label>
                  <Input
                    id="detail-note"
                    value={cashNote}
                    onChange={(e) => setCashNote(e.target.value)}
                    placeholder="e.g. Cash paid in person"
                    className="h-8 text-xs"
                  />
                </div>
                <div className="flex gap-2">
                  <Button
                    size="sm" className="h-7 px-2 text-xs flex-1"
                    disabled={activating_ || !cashPlanId}
                    onClick={handleActivate}
                  >
                    {activating_ ? <Loader2 className="h-3 w-3 animate-spin" /> : "Activate subscription"}
                  </Button>
                  <Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
                    onClick={() => { setActivating(false); setCashPlanId(""); setCashNote(""); }}>
                    Cancel
                  </Button>
                </div>
              </div>
            )}

            {/* Cancel subscription confirm */}
            {confirming && (
              <div className="flex items-center gap-2 pt-2 border-t">
                <span className="text-xs text-destructive font-medium">Cancel subscription permanently?</span>
                <Button
                  size="sm" variant="destructive" className="h-7 px-2 text-xs"
                  disabled={cancelling_} onClick={handleCancel}
                >
                  {cancelling_ ? <Loader2 className="h-3 w-3 animate-spin" /> : "Confirm"}
                </Button>
                <Button size="sm" variant="ghost" className="h-7 px-2 text-xs"
                  onClick={() => setConfirming(false)}>Back</Button>
              </div>
            )}
          </CardContent>
        </Card>

      </main>
    </div>
  );
}
```

Run `pnpm lint` — must pass.

**Commit:** `feat(studios-list): IssuerStudioDetailPage with full action panel`

---

## 8. Wire Studio Detail into Router & Exports

**File:** `frontend/src/features/platform/index.ts`

Add export:

```typescript
export { IssuerStudioDetailPage } from "./components/IssuerStudioDetailPage";
```

**File:** `frontend/src/app/router.tsx`

Import `IssuerStudioDetailPage`:

```typescript
import {
  IssuerDashboardPage,
  IssuerStudioListPage,
  IssuerStudioDetailPage,
  PlanManagementPage,
  SubscriptionOversightPage,
  PlatformReferralPage,
  IndustryReportsPage,
} from "@/features/platform";
```

Add a child route under `platform/studios`:

```typescript
{ path: "platform",
  element: <RoleGuard allowedRoles={[Role.Issuer]} />,
  children: [
    { index: true,                    element: <IssuerDashboardPage /> },
    { path: "studios",                element: <IssuerStudioListPage /> },
    { path: "studios/:studioId",      element: <IssuerStudioDetailPage /> },  // ← NEW
    { path: "plans",                  element: <PlanManagementPage /> },
    { path: "subscriptions",          element: <SubscriptionOversightPage /> },
    { path: "referrals",              element: <PlatformReferralPage /> },
    { path: "reports",                element: <IndustryReportsPage /> },
  ],
},
```

**File:** `frontend/src/features/platform/components/IssuerStudioListPage.tsx`

Add a "View →" `Link` button as the FIRST item in each row's button group:

```tsx
import { Link } from "react-router-dom";
// ...

{/* 0. View detail — always visible, never hidden by anyExpanded */}
<Link to={`/platform/studios/${studio.id}`}>
  <Button size="sm" variant="ghost" className="h-7 px-2 text-xs gap-1"
    title="View studio details">
    <ExternalLink className="h-3.5 w-3.5" />
    View
  </Button>
</Link>
```

Place this BEFORE the `anyExpanded` checks — the View link is always visible
regardless of which form is expanded.

Run `pnpm lint` — must pass.

**Commit:** `feat(router): wire /platform/studios/:studioId, add View link in list`

---

## 9. Tests — Update `IssuerStudioListPage.test.tsx`

**File:** `frontend/src/features/platform/__tests__/IssuerStudioListPage.test.tsx`

### 9a. Update existing tests for copy changes

Some existing tests assert against the old button label "Cancel sub" or the old
badge text "Trialing". Update them:

- Search for `"Cancel sub"` in the test file and replace with `"Cancel Subscription"`.
- Search for `"Trialing"` in contexts where the badge text is being asserted and
  replace with `"In Trial"`.
- Search for `"Extend trial"` in button-name queries and update to
  `/extend trial|grant extension/i` (case-insensitive, OR pattern) so both
  states pass.

### 9b. Add new tests

```typescript
it("shows 'In Trial' badge instead of 'Trialing'", async () => {
  // STUDIO_SUSPENDED has SUB_TRIALING in our seed
  renderPage();
  await screen.findByText("Suspended Studio");
  // Badge element specifically — not the filter option
  const badges = screen.getAllByText("In Trial", { selector: "span" });
  expect(badges.length).toBeGreaterThan(0);
});

it("does not render 'No plan' for studios in trial", async () => {
  renderPage();
  await screen.findByText("Suspended Studio");
  // The meta line should show "In Trial" not "No plan"
  expect(screen.queryByText(/no plan/i)).not.toBeInTheDocument();
});

it("View button links to studio detail page", async () => {
  renderPage();
  await screen.findByText("Ink Soul");
  const viewLinks = screen.getAllByRole("link", { name: /view/i });
  expect(viewLinks[0]).toHaveAttribute("href", `/platform/studios/s1`);
});

it("Cancel Subscription button appears last in the button group for Active studios", async () => {
  renderPage();
  await screen.findByText("Ink Soul");
  const cancelBtn = screen.getByRole("button", { name: /cancel subscription/i });
  expect(cancelBtn).toBeInTheDocument();
  // Verify Suspend appears in the DOM before Cancel (button ordering)
  const allButtons = screen.getAllByRole("button");
  const suspendIdx = allButtons.findIndex((b) => /suspend/i.test(b.textContent ?? ""));
  const cancelIdx  = allButtons.findIndex((b) => /cancel subscription/i.test(b.textContent ?? ""));
  expect(suspendIdx).toBeLessThan(cancelIdx);
});

it("shows skeleton cards while loading, not a spinner", () => {
  renderPage();
  // Skeleton elements are rendered as div.animate-pulse
  expect(document.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  expect(screen.queryByText("Loading…")).not.toBeInTheDocument();
});

it("plan filter shows available plans in dropdown", async () => {
  renderPage();
  await screen.findByText("Ink Soul");
  const planSelect = screen.getByDisplayValue("All plans");
  expect(planSelect).toBeInTheDocument();
  // Plans are from MSW seed: "Starter"
  expect(screen.getByRole("option", { name: "Starter" })).toBeInTheDocument();
});
```

### 9c. Add `IssuerStudioDetailPage.test.tsx`

**New file:** `frontend/src/features/platform/__tests__/IssuerStudioDetailPage.test.tsx`

```typescript
import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { platformApi } from "@/features/platform/platformApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { billingApi } from "@/features/billing/billingApi";
import { IssuerStudioDetailPage } from "@/features/platform/components/IssuerStudioDetailPage";
import type { StudioResponse } from "@/features/studios/studiosApi";
import type { PlatformSubscriptionResponse } from "@/features/platform/platform.types";
import type { PlanResponse } from "@/features/billing/billing.types";

const STUDIO: StudioResponse = {
  id:                   "s1",
  name:                 "Ink Soul",
  slug:                 "ink-soul",
  city:                 "Porto",
  latitude:             41.1,
  longitude:            -8.6,
  showPlatformBranding: true,
  allowBrandingRemoval: false,
  trialExpiresAt:       new Date(Date.now() + 14 * 86_400_000).toISOString(),
  createdAt:            "2024-01-01T00:00:00Z",
  isActive:             true,
};

const SUB: PlatformSubscriptionResponse = {
  studioId:         "s1",
  studioName:       "Ink Soul",
  studioSlug:       "ink-soul",
  subscriptionId:   "sub-1",
  status:           "Active",
  planName:         "Pro",
  trialExpiresAt:   new Date(Date.now() + 30 * 86_400_000).toISOString(),
  currentPeriodEnd: new Date(Date.now() + 30 * 86_400_000).toISOString(),
};

const PLANS: PlanResponse[] = [
  {
    id:                    "plan-1",
    name:                  "Starter",
    billingInterval:       "Monthly",
    priceMonthly:          29,
    priceYearly:           290,
    yearlyDiscountPercent: 17,
    allowBrandingRemoval:  false,
  },
];

const server = setupServer(
  http.get("http://localhost/api/v1/studios/s1", () => HttpResponse.json(STUDIO)),
  http.get("http://localhost/api/v1/platform/subscriptions", () => HttpResponse.json([SUB])),
  http.get("http://localhost/api/v1/billing/plans", () => HttpResponse.json(PLANS)),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth:                         authReducer,
      [platformApi.reducerPath]:    platformApi.reducer,
      [studiosApi.reducerPath]:     studiosApi.reducer,
      [billingApi.reducerPath]:     billingApi.reducer,
    },
    middleware: (gd) =>
      gd().concat(platformApi.middleware, studiosApi.middleware, billingApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u4", email: "issuer@platform.test" }, token: "fake", tenantId: null, role: "issuer", pendingReferralCode: null } as any,
    },
  });
}

function renderPage(studioId = "s1") {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[`/platform/studios/${studioId}`]}>
        <Routes>
          <Route path="/platform/studios/:studioId" element={<IssuerStudioDetailPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

describe("IssuerStudioDetailPage", () => {
  it("renders the studio name", async () => {
    renderPage();
    expect(await screen.findByText("Ink Soul")).toBeInTheDocument();
  });

  it("renders Active badge", async () => {
    renderPage();
    await screen.findByText("Ink Soul");
    expect(screen.getAllByText("Active").length).toBeGreaterThan(0);
  });

  it("renders city and registration date", async () => {
    renderPage();
    await screen.findByText("Porto");
    expect(screen.getByText("Porto")).toBeInTheDocument();
  });

  it("renders a back link to /platform/studios", async () => {
    renderPage();
    await screen.findByText("Ink Soul");
    expect(screen.getByRole("link", { name: /studios/i })).toHaveAttribute("href", "/platform/studios");
  });

  it("shows 404 message for unknown studio id", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/unknown", () =>
        HttpResponse.json({ message: "Not found" }, { status: 404 }),
      ),
    );
    renderPage("unknown");
    expect(await screen.findByText(/studio not found/i)).toBeInTheDocument();
  });

  it("shows Suspend button for active studios", async () => {
    renderPage();
    await screen.findByText("Ink Soul");
    expect(screen.getByRole("button", { name: /suspend/i })).toBeInTheDocument();
  });
});
```

Run `pnpm test` — all tests must pass.

**Commit:** `test(studios-list): update tests for copy changes, add detail page tests`

---

## 10. Backend Test — `GetStudioByIdQuery`

**New file:** `tests/Pena_e_Arte.UnitTests/Studios/GetStudioByIdHandlerTests.cs`

Follow the pattern from `GetStudiosHandlerTests.cs` (or similar in that directory):

```csharp
using FluentAssertions;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;
using Xunit;

namespace Pena_e_Arte.UnitTests.Studios;

public class GetStudioByIdHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetStudioByIdHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ExistingStudio_ReturnsStudioResponse()
    {
        Studio studio = new()
        {
            Id        = Guid.NewGuid(),
            Name      = "Ink Soul",
            Slug      = "ink-soul",
            City      = "Porto",
            IsActive  = true,
            CreatedAt = DateTime.UtcNow.AddDays(-30),
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
        };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        StudioResponse result = await CreateSut().Handle(new GetStudioByIdQuery(studio.Id), default);

        result.Id.Should().Be(studio.Id);
        result.Name.Should().Be("Ink Soul");
        result.Slug.Should().Be("ink-soul");
    }

    [Fact]
    public async Task Handle_NonExistentStudio_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetStudioByIdQuery(Guid.NewGuid()), default);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_SuspendedStudio_ReturnsWithIsActiveFalse()
    {
        Studio studio = new()
        {
            Id       = Guid.NewGuid(),
            Name     = "Closed Studio",
            Slug     = "closed",
            City     = "Lisbon",
            IsActive = false,
            CreatedAt = DateTime.UtcNow.AddDays(-60),
            TrialExpiresAt = DateTime.UtcNow.AddDays(-10),
        };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        StudioResponse result = await CreateSut().Handle(new GetStudioByIdQuery(studio.Id), default);

        result.IsActive.Should().BeFalse();
    }
}
```

> Note: Check that `FakeDbContext.Create()` and the `Studio` entity constructor
> follow the same patterns used in other test files in
> `tests/Pena_e_Arte.UnitTests/Studios/`. Adapt if needed — do not invent a
> pattern that doesn't exist.

Run `dotnet test` — all tests must pass.

**Commit:** `test(studios): GetStudioByIdHandler unit tests`

---

## 11. Final Verification

1. `dotnet build` — zero errors, zero warnings on new files.
2. `dotnet test` — all tests pass.
3. `pnpm --dir frontend lint` — zero errors.
4. `pnpm --dir frontend test` — all tests pass.
5. Verify `canExtendTrial = subStatus !== "Active"` still holds in `StudioRow`
   (the Trialing edge case where trial has expired but status is now GracePeriod
   is handled by the `periodText` logic, not by hiding the button).
6. Verify the `GET /api/v1/studios/:id` route is registered AFTER `/me` and
   `/map` in `StudioEndpoints.cs` to avoid shadowing:
   `grep -n "MapGet" Pena_e_Arte.API/Endpoints/StudioEndpoints.cs`
7. Verify no `IgnoreQueryFilters()` call was added to any handler NOT in the
   approved table in `architecture.md`.
8. `git log --oneline -15` — confirm all task commits are present.

---

## Reference: Audit Issue → Task Map

| Audit Issue                                       | Task     |
|---------------------------------------------------|----------|
| "Trialing" → "In Trial"                           | Task 1a  |
| "No plan" for trialing studios                    | Task 1b  |
| Contradictory "Trial expired" in Grace Period row | Task 1c  |
| "Cancel sub" abbreviation                         | Task 1d  |
| "Extend trial" on expired trial                   | Task 1e  |
| No icons on Extend trial / Cancel Subscription    | Task 2a  |
| Activate has no visual priority                   | Task 2b  |
| Destructive action appears first                  | Task 2c  |
| Badge wraps to second line (layout bug)           | Task 3   |
| Jarring spinner loading state                     | Task 4   |
| No plan filter                                    | Task 5   |
| No drill-down / studio detail view (#1 critical)  | Tasks 6–8|
| Date label inconsistency                          | Task 1c  |
| canExtendTrial edge case for Grace Period         | Task 1e  |
