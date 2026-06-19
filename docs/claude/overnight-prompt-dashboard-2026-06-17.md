# Overnight Prompt — Issuer Dashboard Overhaul (2026-06-17)

> **Scope:** 22 data, navigation, chart, and UX issues on the issuer platform dashboard.
> All changes are in the issuer frontend + the `GetPlatformStatsHandler` backend.
>
> Work in order — Tasks 1–3 are backend and establish the data contract; Tasks
> 4–9 are frontend; Tasks 10–11 are tests; Task 12 is verification.
> Commit after each numbered task.
>
> Do NOT add new npm packages. Do NOT add new NuGet packages.
> Do NOT modify the database schema (no new migrations).

---

## 0. Mandatory Reading (Do This First)

```
CLAUDE.md
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/conventions.md
docs/claude/architecture.md
```

Then read these source files to lock the patterns into context:

```
Pena_e_Arte.Contracts/Responses/PlatformStatsResponse.cs
Pena_e_Arte.Application/Platform/Queries/GetPlatformStatsQuery.cs
Pena_e_Arte.Application/Platform/Queries/GetMrrHistoryQuery.cs
frontend/src/features/platform/platform.types.ts
frontend/src/features/platform/platformApi.ts
frontend/src/features/platform/components/IssuerDashboardPage.tsx
frontend/src/features/platform/components/MrrChart.tsx
frontend/src/layouts/IssuerLayout.tsx
frontend/src/features/platform/__tests__/IssuerDashboardPage.test.tsx
tests/Pena_e_Arte.UnitTests/Platform/GetPlatformStatsHandlerTests.cs
```

Also find the API endpoint that maps to `GET /api/v1/platform/mrr-history` — grep for
`"mrr-history"` or `"mrrHistory"` in `Pena_e_Arte.API/Endpoints/`. You will need that
file in Task 3.

---

## 1. Bug Fix — `GetPlatformStatsHandler` (Backend)

### What is wrong

`totalStudios = studios.Count(s => s.IsActive)`. However the three secondary
counts (`activeSubscriptions`, `trialStudios`, `gracePeriodStudios`) iterate ALL
studios regardless of `IsActive`. So a studio with `IsActive = true` and
`PastDue` subscription is counted in `totalStudios` but in none of the
secondary counts — the numbers do not add up.

Additionally, `PastDueStudios`, `CancelledStudios`, `SuspendedStudios`, and
`MrrGrowthPercent` are entirely absent from the response, making certain KPIs
impossible to display.

### Changes

**File:** `Pena_e_Arte.Contracts/Responses/PlatformStatsResponse.cs`

Replace the existing record with:

```csharp
namespace Pena_e_Arte.Contracts.Responses;

/// <summary>
/// Platform-wide aggregate statistics. All counts are point-in-time snapshots.
/// TotalStudios = ActiveSubscriptions + TrialStudios + GracePeriodStudios
///              + PastDueStudios + CancelledStudios + SuspendedStudios
///              (every studio falls into exactly one bucket).
/// </summary>
public record PlatformStatsResponse(
    int     TotalStudios,
    int     ActiveSubscriptions,
    int     TrialStudios,
    int     GracePeriodStudios,
    int     PastDueStudios,
    int     CancelledStudios,
    int     SuspendedStudios,
    decimal Mrr,
    double  MrrGrowthPercent,
    double  TrialConversionRate,
    int     NewStudiosThisMonth);
```

**File:** `Pena_e_Arte.Application/Platform/Queries/GetPlatformStatsQuery.cs`

Replace the `Handle` method body:

```csharp
public async Task<PlatformStatsResponse> Handle(GetPlatformStatsQuery query, CancellationToken ct)
{
    DateTime now        = DateTime.UtcNow;
    DateTime monthStart = new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
    DateTime lastMonth  = monthStart.AddMonths(-1);

    // IgnoreQueryFilters approved: usage #4 — platform KPI aggregate, IssuerOnly. See architecture.md.
    List<Studio> studios = await db.Studios
        .IgnoreQueryFilters()
        .Include(s => s.Subscription)
            .ThenInclude(sub => sub!.Plan)
        .ToListAsync(ct);

    // Suspended = manually deactivated by issuer (IsActive = false). These studios are still
    // in the DB but invisible on the platform. They are NOT included in any subscription bucket.
    int suspendedStudios = studios.Count(s => !s.IsActive);

    // All subsequent counts operate only on active studios (IsActive = true).
    List<Studio> active = studios.Where(s => s.IsActive).ToList();

    int totalStudios        = active.Count;
    int activeSubscriptions = active.Count(s => s.Subscription?.Status == SubscriptionStatus.Active);
    int trialStudios        = active.Count(s =>
        s.Subscription?.Status == SubscriptionStatus.Trialing
        || (s.Subscription is null && s.TrialExpiresAt > now));
    int gracePeriodStudios  = active.Count(s => s.Subscription?.Status == SubscriptionStatus.GracePeriod);
    int pastDueStudios      = active.Count(s => s.Subscription?.Status == SubscriptionStatus.PastDue);
    int cancelledStudios    = active.Count(s => s.Subscription?.Status == SubscriptionStatus.Cancelled);

    // MRR — active subscriptions only, sum of plan monthly price.
    decimal mrr = active
        .Where(s => s.Subscription?.Status == SubscriptionStatus.Active && s.Subscription.Plan is not null)
        .Sum(s => s.Subscription!.Plan!.PriceMonthly);

    // MRR growth: compare with last calendar month.
    decimal lastMonthMrr = active
        .Where(s =>
            s.Subscription is not null
            && s.Subscription.Plan is not null
            && s.Subscription.CreatedAt < monthStart       // existed last month
            && s.Subscription.CurrentPeriodEnd >= lastMonth // was active last month
            && s.Subscription.Status == SubscriptionStatus.Active)
        .Sum(s => s.Subscription!.Plan!.PriceMonthly);

    double mrrGrowthPercent = lastMonthMrr == 0
        ? (mrr > 0 ? 100.0 : 0.0)
        : Math.Round((double)((mrr - lastMonthMrr) / lastMonthMrr) * 100, 1);

    int conversionDenominator = activeSubscriptions + trialStudios + gracePeriodStudios;
    double trialConversionRate = conversionDenominator > 0
        ? Math.Round((double)activeSubscriptions / conversionDenominator, 4)
        : 0;

    int newStudiosThisMonth = active.Count(s => s.CreatedAt >= monthStart);

    return new PlatformStatsResponse(
        totalStudios,
        activeSubscriptions,
        trialStudios,
        gracePeriodStudios,
        pastDueStudios,
        cancelledStudios,
        suspendedStudios,
        mrr,
        mrrGrowthPercent,
        trialConversionRate,
        newStudiosThisMonth);
}
```

> Note on `lastMonthMrr`: this is an approximation computed from current
> subscription data, not a true historical snapshot. It undercounts if any
> subscriptions were active last month but have since been cancelled.
> A true historical MRR requires a `SubscriptionRevenue` ledger table, which
> is out of scope. The approximation is acceptable for the current scale and
> is better than showing no growth indicator at all.

Run `dotnet build` — must succeed.

**Commit:** `fix(platform): add PastDue/Cancelled/Suspended/MrrGrowth to stats, fix total math`

---

## 2. Feature — `GetMrrHistoryQuery` Range Parameter (Backend)

The chart needs to default to 3 months instead of 12, and support a selector.

**File:** `Pena_e_Arte.Application/Platform/Queries/GetMrrHistoryQuery.cs`

Change the query record to accept `Months`:

```csharp
public record GetMrrHistoryQuery(int Months = 12) : IRequest<List<MrrDataPointResponse>>;
```

In `Handle`, replace the hardcoded `12` with `query.Months`:

```csharp
var result = new List<MrrDataPointResponse>(query.Months);

for (int i = query.Months - 1; i >= 0; i--)
{
    // ... rest unchanged
}
```

Clamp `Months` at the top of the method to prevent abuse:

```csharp
int months = Math.Clamp(query.Months, 1, 24);
```

Run `dotnet build` — must succeed.

**Commit:** `feat(platform): add Months parameter to GetMrrHistoryQuery`

---

## 3. Feature — MRR History API Endpoint (Backend)

Find the endpoint that handles `GET /api/v1/platform/mrr-history` in
`Pena_e_Arte.API/Endpoints/`. Grep for `"mrr-history"` to locate it.

Update the handler lambda to read the optional `months` query parameter and
pass it to the query:

```csharp
// Existing pattern (approximately):
group.MapGet("mrr-history", async (ISender sender, CancellationToken ct) =>
    Results.Ok(await sender.Send(new GetMrrHistoryQuery(), ct)))
    .RequireAuthorization("IssuerOnly");

// Updated:
group.MapGet("mrr-history", async (
    ISender sender,
    int? months,          // bound from query string: ?months=3
    CancellationToken ct) =>
    Results.Ok(await sender.Send(new GetMrrHistoryQuery(Math.Clamp(months ?? 12, 1, 24)), ct)))
    .RequireAuthorization("IssuerOnly");
```

Run `dotnet build` — must succeed.

**Commit:** `feat(platform): mrr-history endpoint accepts ?months query param`

---

## 4. Fix — `IssuerLayout.tsx` (Frontend)

Two problems in the layout:
- `NAV_ITEMS` contains a "Notifications" text link with a Bell icon AND
  `<NotificationBell />` is also rendered in the same header — duplicate.
- "Log out" is a persistent top-level button — should be in a user menu dropdown.

**File:** `frontend/src/layouts/IssuerLayout.tsx`

### 4a. Remove "Notifications" from `NAV_ITEMS`

Delete the `{ label: "Notifications", href: "/notifications", icon: <Bell .../> }` entry
from `NAV_ITEMS`. The `Bell` import can be removed if nothing else uses it.
`<NotificationBell />` in the header stays — it is the correct pattern.

### 4b. Move Log Out into a `UserMenu` dropdown

Replace the standalone `<Button onClick={handleLogout}>Log out</Button>` with a
controlled dropdown that opens from the user avatar area. Keep this
**entirely in `IssuerLayout.tsx`** — do not modify `UserChip.tsx`.

Add `useState` import. Create a `UserMenu` component inside the file:

```tsx
import { useState, useEffect, useRef } from "react";

function UserMenu({ onLogout }: { onLogout: () => void }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  // Close when clicking outside
  useEffect(() => {
    if (!open) return;
    function handleClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpen(false);
      }
    }
    document.addEventListener("mousedown", handleClick);
    return () => document.removeEventListener("mousedown", handleClick);
  }, [open]);

  return (
    <div ref={ref} className="relative">
      <button
        onClick={() => setOpen((v) => !v)}
        className="flex items-center gap-1 rounded-md px-2 py-1.5 text-sm text-muted-foreground hover:text-foreground hover:bg-muted transition-colors"
        aria-expanded={open}
        aria-haspopup="true"
      >
        <UserChip />
        <ChevronDown className="h-3 w-3 opacity-50" />
      </button>

      {open && (
        <div className="absolute right-0 top-full mt-1 w-40 rounded-md border bg-background shadow-md z-50 py-1">
          <button
            onClick={() => { setOpen(false); onLogout(); }}
            className="flex items-center gap-2 w-full px-3 py-2 text-sm text-muted-foreground hover:text-foreground hover:bg-muted transition-colors"
          >
            <LogOut className="h-4 w-4" />
            Log out
          </button>
        </div>
      )}
    </div>
  );
}
```

Add `ChevronDown` to the `lucide-react` import line.

In `IssuerLayout`, replace the old `<UserChip />` + separator + Log Out button block with:

```tsx
<div className="ml-auto flex items-center gap-3">
  <NotificationBell />
  <UserMenu onLogout={handleLogout} />
</div>
```

Remove the `<div className="w-px h-5 bg-border" />` separator — it's no longer needed.

Run `pnpm lint` in `frontend/` — must pass.

**Commit:** `fix(layout): remove duplicate Notifications nav item, move Log Out into user dropdown`

---

## 5. Update — `platform.types.ts` (Frontend)

**File:** `frontend/src/features/platform/platform.types.ts`

Update `PlatformStatsResponse` to match the new backend response:

```typescript
export interface PlatformStatsResponse {
  totalStudios:        number;
  activeSubscriptions: number;
  trialStudios:        number;
  gracePeriodStudios:  number;
  pastDueStudios:      number;
  cancelledStudios:    number;
  suspendedStudios:    number;
  mrr:                 number;
  mrrGrowthPercent:    number;
  trialConversionRate: number;
  newStudiosThisMonth: number;
}
```

**File:** `frontend/src/features/platform/platformApi.ts`

Update `getMrrHistory` to accept an optional `months` argument:

```typescript
getMrrHistory: builder.query<MrrDataPoint[], number | void>({
  query: (months) => months ? `platform/mrr-history?months=${months}` : "platform/mrr-history",
  providesTags: ["MrrHistory"],
}),
```

The exported hook becomes `useGetMrrHistoryQuery(months?: number)`. Callers
pass the months value as the first argument (RTK Query hook argument).

**Commit:** `feat(platform): update types + api hook for new stats fields and mrr range param`

---

## 6. Overhaul — `IssuerDashboardPage.tsx` (Frontend)

This is the largest change. Apply every sub-section below.

**File:** `frontend/src/features/platform/components/IssuerDashboardPage.tsx`

### 6a. Fix `formatCurrency` — international format

```typescript
function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("en-GB", {
    style:    "currency",
    currency: "EUR",
    minimumFractionDigits: 0,
    maximumFractionDigits: 2,
  }).format(amount);
}
```

This produces `€245` or `€245.50` — scannable at a glance.

### 6b. Add `subtitle` to `KpiCard`

```typescript
interface KpiCardProps {
  label:    string;
  value:    string | number;
  icon:     React.ReactNode;
  subtitle?: string;
  href?:    string;
  accent?:  KpiAccent;
}

function KpiCard({ label, value, icon, subtitle, href, accent = "default" }: KpiCardProps) {
  const inner = (
    <Card className={href ? "hover:bg-muted/50 transition-colors" : ""}>
      <CardContent className="p-4 flex items-center justify-between gap-4">
        <div>
          <p className="text-xs text-muted-foreground">{label}</p>
          <p className="text-2xl font-semibold tracking-tight">{value}</p>
          {subtitle && (
            <p className="text-[10px] text-muted-foreground mt-0.5">{subtitle}</p>
          )}
        </div>
        <div className={ACCENT_ICON_COLOR[accent]}>{icon}</div>
      </CardContent>
    </Card>
  );

  return href ? <Link to={href}>{inner}</Link> : inner;
}
```

### 6c. Add "danger" accent for critical states

```typescript
type KpiAccent = "default" | "info" | "warning" | "success" | "danger";

const ACCENT_ICON_COLOR: Record<KpiAccent, string> = {
  default: "text-muted-foreground",
  info:    "text-blue-500",
  warning: "text-amber-500",
  success: "text-emerald-500",
  danger:  "text-red-500",
};
```

### 6d. Suppress "New This Month" when it equals nearly all studios

When `newStudiosThisMonth / totalStudios > 0.5`, the number is likely test
data or a launch-day spike and is misleading. Show a caveat:

```typescript
const newThisMonthCaveat =
  stats && stats.totalStudios > 0 && stats.newStudiosThisMonth / stats.totalStudios > 0.5
    ? "incl. test data"
    : "this calendar month";
```

Pass `newThisMonthCaveat` as `subtitle` to the "New This Month" KPI card.

### 6e. KPI grid — update the grid with all new cards

Replace the entire KPI grid section (both `<div className="grid ...">` blocks)
with this layout:

```tsx
<div className="space-y-3">
  {/* Row 1 — totals */}
  <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
    <KpiCard
      label="Total Studios"
      value={stats?.totalStudios ?? 0}
      icon={<Building2 className="h-6 w-6" />}
      subtitle="all tenants"
      href="/platform/studios"
    />
    <KpiCard
      label="Active Subscriptions"
      value={stats?.activeSubscriptions ?? 0}
      icon={<CreditCard className="h-6 w-6" />}
      subtitle="current"
      href="/platform/subscriptions?status=Active"
      accent="success"
    />
    <KpiCard
      label="MRR"
      value={formatCurrency(stats?.mrr ?? 0)}
      icon={<TrendingUp className="h-6 w-6" />}
      subtitle={
        stats?.mrrGrowthPercent !== undefined
          ? `${stats.mrrGrowthPercent >= 0 ? "+" : ""}${stats.mrrGrowthPercent.toFixed(1)}% vs last month`
          : undefined
      }
      accent={stats?.mrrGrowthPercent != null && stats.mrrGrowthPercent > 0 ? "success" : "default"}
    />
    <KpiCard
      label="ARPU"
      value={
        stats && stats.activeSubscriptions > 0
          ? formatCurrency(stats.mrr / stats.activeSubscriptions)
          : "—"
      }
      icon={<Users className="h-6 w-6" />}
      subtitle="MRR ÷ active"
      accent="info"
    />
  </div>

  {/* Row 2 — pipeline */}
  <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
    <KpiCard
      label="Trialing"
      value={stats?.trialStudios ?? 0}
      icon={<Clock className="h-6 w-6" />}
      subtitle="current"
      href="/platform/subscriptions?status=Trialing"
      accent="info"
    />
    <KpiCard
      label="Grace Period"
      value={stats?.gracePeriodStudios ?? 0}
      icon={<AlertCircle className="h-6 w-6" />}
      subtitle="current"
      href="/platform/subscriptions?status=GracePeriod"
      accent="warning"
    />
    <KpiCard
      label="Past Due"
      value={stats?.pastDueStudios ?? 0}
      icon={<AlertTriangle className="h-6 w-6" />}
      subtitle="current"
      href="/platform/subscriptions?status=PastDue"
      accent="danger"
    />
    <KpiCard
      label="Cancelled"
      value={stats?.cancelledStudios ?? 0}
      icon={<XCircle className="h-6 w-6" />}
      subtitle="current"
      href="/platform/subscriptions?status=Cancelled"
    />
  </div>

  {/* Row 3 — health */}
  <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
    <KpiCard
      label="Trial Conversion"
      value={formatPercent(stats?.trialConversionRate ?? 0)}
      icon={<TrendingUp className="h-6 w-6" />}
      subtitle="active ÷ (active + trial + grace)"
    />
    <KpiCard
      label="New This Month"
      value={stats?.newStudiosThisMonth ?? 0}
      icon={<PlusCircle className="h-6 w-6" />}
      subtitle={newThisMonthCaveat}
      href="/platform/studios"
      accent="success"
    />
    <KpiCard
      label="Suspended"
      value={stats?.suspendedStudios ?? 0}
      icon={<Ban className="h-6 w-6" />}
      subtitle="deactivated by issuer"
      href="/platform/studios"
      accent={stats?.suspendedStudios ? "danger" : "default"}
    />
  </div>
</div>
```

Add `XCircle` and `Ban` to the `lucide-react` import. Remove `BarChart3`,
`Share2`, `Receipt` if they are no longer used anywhere in this file after
the quick nav section is removed (next step).

### 6f. Remove the Quick Navigation section

Delete the entire `QUICK_NAV` array constant and the `{/* Quick nav */}`
JSX block (lines 114–234 in the original file). This section is a duplicate
of the persistent top navbar in `IssuerLayout`.

### 6g. Update the KPI grid skeleton

Update `KpiGridSkeleton` to match the new 3-row layout:

```tsx
function KpiGridSkeleton() {
  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <KpiSkeleton /><KpiSkeleton /><KpiSkeleton /><KpiSkeleton />
      </div>
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <KpiSkeleton /><KpiSkeleton /><KpiSkeleton /><KpiSkeleton />
      </div>
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
        <KpiSkeleton /><KpiSkeleton /><KpiSkeleton />
      </div>
    </div>
  );
}
```

### 6h. Overhaul `AtRiskRow`

Replace the existing `AtRiskRow` function with:

```tsx
function daysUntilExpiry(dateStr: string): number {
  const expiry = new Date(dateStr).getTime();
  const now    = Date.now();
  return Math.ceil((expiry - now) / 86_400_000);
}

function ExpiryLabel({ dateStr, status }: { dateStr: string; status: string }) {
  const days = daysUntilExpiry(dateStr);
  if (status === "PastDue") {
    return (
      <p className="text-xs text-red-600 dark:text-red-400 mt-0.5 font-medium">
        Payment overdue
      </p>
    );
  }
  if (days <= 0) {
    return (
      <p className="text-xs text-red-600 dark:text-red-400 mt-0.5 font-medium">
        Expires today
      </p>
    );
  }
  if (days <= 3) {
    return (
      <p className="text-xs text-amber-600 dark:text-amber-400 mt-0.5 font-medium">
        {days} day{days !== 1 ? "s" : ""} left
      </p>
    );
  }
  return (
    <p className="text-xs text-muted-foreground mt-0.5">
      {days} days left
    </p>
  );
}

function AtRiskRow({ sub }: AtRiskRowProps) {
  const [extending,  setExtending]  = useState(false);
  const [days,       setDays]       = useState("7");
  const [extendTrial, { isLoading }] = useExtendTrialMutation();

  async function handleExtend() {
    const additionalDays = parseInt(days, 10);
    if (isNaN(additionalDays) || additionalDays < 1 || additionalDays > 90) return;
    await extendTrial({ studioId: sub.studioId, additionalDays });
    setExtending(false);
  }

  return (
    <div className="py-2 border-b last:border-0 space-y-1.5">
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="flex items-center gap-1.5 flex-wrap">
            <span className="text-sm font-medium">{sub.studioName}</span>
            <span className="text-xs text-muted-foreground font-mono">{sub.studioSlug}</span>
          </div>
          <div className="flex items-center gap-2 mt-0.5">
            <span
              className={`text-xs px-2 py-0.5 rounded-full font-medium ${
                sub.status === "PastDue"
                  ? "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300"
                  : "bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-300"
              }`}
            >
              {sub.status === "PastDue" ? "Past due" : "Grace period"}
            </span>
            <ExpiryLabel dateStr={sub.trialExpiresAt} status={sub.status} />
          </div>
        </div>

        <div className="flex items-center gap-1.5 shrink-0">
          {!extending && (
            <button
              onClick={() => setExtending(true)}
              className="text-xs px-2 py-1 rounded border hover:bg-muted transition-colors"
            >
              Extend trial
            </button>
          )}
          <Link
            to={`/platform/studios`}
            state={{ highlight: sub.studioId }}
            className="text-xs text-muted-foreground hover:text-foreground transition-colors px-1"
            title="Open studio"
          >
            →
          </Link>
        </div>
      </div>

      {extending && (
        <div className="flex items-center gap-2">
          <input
            type="number"
            min="1"
            max="90"
            value={days}
            onChange={(e) => setDays(e.target.value)}
            className="h-7 w-16 rounded border border-input bg-background px-2 text-xs"
          />
          <span className="text-xs text-muted-foreground">days</span>
          <button
            onClick={handleExtend}
            disabled={isLoading}
            className="text-xs px-2 py-1 rounded bg-primary text-primary-foreground hover:opacity-90 transition-opacity disabled:opacity-50"
          >
            {isLoading ? "…" : "Confirm"}
          </button>
          <button
            onClick={() => setExtending(false)}
            className="text-xs px-2 py-1 rounded hover:bg-muted transition-colors text-muted-foreground"
          >
            Cancel
          </button>
        </div>
      )}
    </div>
  );
}
```

Add `useState` to the React import at the top of `IssuerDashboardPage.tsx`.
Add `useExtendTrialMutation` to the `platformApi` imports.
Add `Input` is NOT needed here — using a native `<input>` to avoid adding the
shadcn import for a minor inline field.

### 6i. Add count badge to At-Risk section title

```tsx
<CardTitle className="text-sm flex items-center gap-2">
  <AlertTriangle className="h-4 w-4 text-amber-500" />
  At-Risk Studios
  {atRisk.length > 0 && (
    <span className="ml-1 text-xs bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-300 px-1.5 py-0.5 rounded-full font-medium">
      {atRisk.length}
    </span>
  )}
</CardTitle>
```

### 6j. Duplicate studio name — highlight slug when names collide

In `AtRiskRow`, detect when two studios share the same name:

```tsx
// Pass this as a prop to AtRiskRow
interface AtRiskRowProps {
  sub:           PlatformSubscriptionResponse;
  hasDuplicate?: boolean;
}
```

In `IssuerDashboardPage`, compute before the return:

```tsx
const atRisk = subscriptions?.filter((s) => AT_RISK_STATUSES.has(s.status)) ?? [];
const atRiskNames = atRisk.map((s) => s.studioName);
```

Pass to `AtRiskRow`:

```tsx
atRisk.map((sub) => (
  <AtRiskRow
    key={sub.studioId}
    sub={sub}
    hasDuplicate={atRiskNames.filter((n) => n === sub.studioName).length > 1}
  />
))
```

In `AtRiskRow`, when `hasDuplicate` is true, render the slug more prominently:

```tsx
<span
  className={`text-xs font-mono ${
    hasDuplicate
      ? "text-foreground font-semibold"
      : "text-muted-foreground"
  }`}
>
  {sub.studioSlug}
</span>
```

Run `pnpm lint` in `frontend/` — must pass.

**Commit:** `fix(dashboard): data accuracy, new KPIs, at-risk urgency, remove quick nav, fix currency`

---

## 7. Overhaul — `MrrChart.tsx` (Frontend)

**File:** `frontend/src/features/platform/components/MrrChart.tsx`

### 7a. Time range selector

Add a `months` state defaulting to 3 (not 12 — avoids the 92%-empty chart):

```tsx
const [months, setMonths] = useState<3 | 6 | 12>(3);
const { data, isLoading } = useGetMrrHistoryQuery(months);
```

Render range buttons above the chart in the `CardHeader`:

```tsx
<CardHeader className="pb-2">
  <div className="flex items-center justify-between gap-2">
    <CardTitle className="text-sm">MRR trend</CardTitle>
    <div className="flex items-center gap-1">
      {([3, 6, 12] as const).map((m) => (
        <button
          key={m}
          onClick={() => setMonths(m)}
          className={`text-[11px] px-2 py-0.5 rounded transition-colors ${
            months === m
              ? "bg-primary text-primary-foreground"
              : "text-muted-foreground hover:text-foreground hover:bg-muted"
          }`}
        >
          {m}m
        </button>
      ))}
    </div>
  </div>
</CardHeader>
```

### 7b. SVG tooltip on hover

Add `useState` for the hovered point:

```tsx
const [tooltip, setTooltip] = useState<{ x: number; y: number; mrr: number; month: string } | null>(null);
```

For each dot, add `onMouseEnter` and `onMouseLeave`:

```tsx
<g
  key={d.month}
  onMouseEnter={() => setTooltip({ x, y, mrr: d.mrr, month: d.month })}
  onMouseLeave={() => setTooltip(null)}
  style={{ cursor: "default" }}
>
  <circle cx={x} cy={y} r={4} style={{ fill: "hsl(var(--primary))" }} />
  {/* Larger invisible hit area */}
  <circle cx={x} cy={y} r={10} fill="transparent" />
  {/* X-axis label */}
  {(i % 2 === 0 || i === n - 1) && (
    <text x={x} y={H - 4} textAnchor="middle" fontSize={9}
          fill="currentColor" fillOpacity={0.5}>
      {fmtMonth(d.month)}
    </text>
  )}
</g>
```

Render the tooltip as SVG elements at the end of the SVG (so it renders on top):

```tsx
{tooltip && (() => {
  const tipW  = 76;
  const tipH  = 28;
  const tipX  = Math.min(Math.max(tooltip.x - tipW / 2, PAD_L), W - PAD_R - tipW);
  const tipY  = tooltip.y - tipH - 6;
  return (
    <g>
      <rect x={tipX} y={tipY} width={tipW} height={tipH}
            rx={4} fill="hsl(var(--popover))" stroke="hsl(var(--border))" strokeWidth={0.5} />
      <text x={tipX + tipW / 2} y={tipY + 10} textAnchor="middle" fontSize={9}
            fill="currentColor" fillOpacity={0.7}>
        {fmtMonth(tooltip.month)}
      </text>
      <text x={tipX + tipW / 2} y={tipY + 21} textAnchor="middle" fontSize={10}
            fontWeight="600" fill="currentColor">
        {fmtY(tooltip.mrr)}
      </text>
    </g>
  );
})()}
```

Pass `tooltip` state setter into the `Chart` component via props, or lift the
SVG into `MrrChart` itself. The cleanest approach is to keep `Chart` and accept
`onHover` and `activeTooltip` as props.

### 7c. Y-axis label

Add a rotated "EUR" label along the Y axis:

```tsx
<text
  x={10}
  y={PAD_T + PH / 2}
  textAnchor="middle"
  fontSize={8}
  fill="currentColor"
  fillOpacity={0.4}
  transform={`rotate(-90, 10, ${PAD_T + PH / 2})`}
>
  EUR
</text>
```

Also increase `PAD_L` from 52 to 60 to accommodate the label without clipping.

Run `pnpm lint` — must pass.

**Commit:** `feat(chart): time range selector (3m default), hover tooltip, Y-axis label`

---

## 8. Feature — `SubscriptionOversightPage.tsx` — Pre-filter from URL (Frontend)

When KPI cards link to `/platform/subscriptions?status=Active`, the
`SubscriptionOversightPage` should read that param and pre-filter the list.

**File:** `frontend/src/features/platform/components/SubscriptionOversightPage.tsx`

Add `useSearchParams` to the React Router import:

```tsx
import { useSearchParams } from "react-router-dom";
```

Inside `SubscriptionOversightPage`:

```tsx
const [searchParams, setSearchParams] = useSearchParams();
const statusFilter = searchParams.get("status") ?? "";

const filtered = subscriptions?.filter((s) =>
  statusFilter ? s.status === statusFilter : true
) ?? [];
```

Add a filter bar above the subscription list. Show all statuses as pills;
the active one is highlighted. Clicking a pill sets/clears the URL param:

```tsx
const ALL_STATUSES = ["Active", "Trialing", "GracePeriod", "PastDue", "Cancelled", "NoSubscription"];

<div className="flex flex-wrap gap-2 mb-4">
  <button
    onClick={() => setSearchParams({})}
    className={`text-xs px-2.5 py-1 rounded-full border transition-colors ${
      !statusFilter ? "bg-primary text-primary-foreground border-primary" : "hover:bg-muted"
    }`}
  >
    All ({subscriptions?.length ?? 0})
  </button>
  {ALL_STATUSES.map((s) => {
    const count = subscriptions?.filter((sub) => sub.status === s).length ?? 0;
    if (count === 0) return null;
    return (
      <button
        key={s}
        onClick={() => setSearchParams({ status: s })}
        className={`text-xs px-2.5 py-1 rounded-full border transition-colors ${
          statusFilter === s
            ? "bg-primary text-primary-foreground border-primary"
            : "hover:bg-muted"
        }`}
      >
        {s} ({count})
      </button>
    );
  })}
</div>
```

Replace the `subscriptions?.map(...)` render call with `filtered.map(...)`.

Update the header count:

```tsx
{subscriptions && (
  <span className="text-xs text-muted-foreground ml-1">
    {filtered.length === subscriptions.length
      ? `(${subscriptions.length})`
      : `(${filtered.length} of ${subscriptions.length})`}
  </span>
)}
```

Run `pnpm lint` — must pass.

**Commit:** `feat(subscriptions): pre-filter from URL ?status= param, add filter pill bar`

---

## 9. Verify Architecture.md (Docs)

`GetMrrHistoryQuery.cs` has a comment claiming `IgnoreQueryFilters` is used
("usage #5") but the call is NOT present in the code — only `AsNoTracking` is.
`Subscriptions` is not a `TenantEntity` and does not have a global query filter.

Find the comment in `GetMrrHistoryQuery.cs` and remove/correct it:

```csharp
// IssuerOnly endpoint — no tenant filter on Subscriptions entity (not a TenantEntity).
```

Also check: `GetMrrHistoryHandler` is not in the `IgnoreQueryFilters()` Approved
Usages table in `architecture.md`. If it is currently listed there incorrectly,
remove it. If it is not listed (correct), no action needed.

**Commit:** `docs: correct misleading IgnoreQueryFilters comment in GetMrrHistoryQuery`

---

## 10. Tests — Backend

**File:** `tests/Pena_e_Arte.UnitTests/Platform/GetPlatformStatsHandlerTests.cs`

### 10a. Update the existing tests

- All tests that call `.Handle(...)` and assert on the result now need to use
  the new record shape (which has 11 fields, not 7). If any test creates a
  `PlatformStatsResponse` directly, update the constructor call.
- No existing test should break because the new fields are additive and the
  handler computes them. Just verify `dotnet test` passes after the backend
  changes in Tasks 1–3.

### 10b. Add new tests

Add these test cases to `GetPlatformStatsHandlerTests`:

```csharp
[Fact]
public async Task Handle_WithPastDueStudio_CountsPastDueSeparately()
{
    Studio studio = SeedStudio(isActive: true);
    await _db.SaveChangesAsync();

    _db.Subscriptions.Add(new Subscription
    {
        StudioId         = studio.Id,
        Status           = SubscriptionStatus.PastDue,
        TrialExpiresAt   = DateTime.UtcNow.AddDays(-5),
        CurrentPeriodEnd = DateTime.UtcNow.AddDays(-5),
    });
    await _db.SaveChangesAsync();

    PlatformStatsResponse result = await CreateSut().Handle(new GetPlatformStatsQuery(), default);

    result.PastDueStudios.Should().Be(1);
    result.ActiveSubscriptions.Should().Be(0);
    result.GracePeriodStudios.Should().Be(0);
}

[Fact]
public async Task Handle_WithSuspendedStudio_CountsSuspendedSeparately()
{
    SeedStudio(isActive: false); // suspended
    SeedStudio(isActive: true);  // active trial
    await _db.SaveChangesAsync();

    PlatformStatsResponse result = await CreateSut().Handle(new GetPlatformStatsQuery(), default);

    result.SuspendedStudios.Should().Be(1);
    result.TotalStudios.Should().Be(1); // suspended is excluded from total
}

[Fact]
public async Task Handle_TotalStudios_EqualsActivePlusTrial_PlusGrace_PlusPastDue_PlusCancelled()
{
    Studio s1 = SeedStudio(isActive: true);
    Studio s2 = SeedStudio(isActive: true);
    Studio s3 = SeedStudio(isActive: true);
    await _db.SaveChangesAsync();

    _db.Subscriptions.Add(new Subscription { StudioId = s1.Id, Status = SubscriptionStatus.Active,      CurrentPeriodEnd = DateTime.UtcNow.AddDays(30), TrialExpiresAt = DateTime.UtcNow.AddDays(30) });
    _db.Subscriptions.Add(new Subscription { StudioId = s2.Id, Status = SubscriptionStatus.PastDue,     CurrentPeriodEnd = DateTime.UtcNow.AddDays(-2), TrialExpiresAt = DateTime.UtcNow.AddDays(-2) });
    _db.Subscriptions.Add(new Subscription { StudioId = s3.Id, Status = SubscriptionStatus.Cancelled,   CurrentPeriodEnd = DateTime.UtcNow.AddDays(-5), TrialExpiresAt = DateTime.UtcNow.AddDays(-5) });
    await _db.SaveChangesAsync();

    PlatformStatsResponse result = await CreateSut().Handle(new GetPlatformStatsQuery(), default);

    int bucketSum = result.ActiveSubscriptions + result.TrialStudios
                  + result.GracePeriodStudios  + result.PastDueStudios
                  + result.CancelledStudios;
    bucketSum.Should().Be(result.TotalStudios);
}

[Fact]
public async Task Handle_MrrGrowthPercent_IsPositiveWhenMrrIncreasedFromLastMonth()
{
    // Subscription created last month and still active this month → appears in both periods.
    Studio studio = SeedStudio(isActive: true);
    Plan   plan   = new() { Name = "Pro", BillingInterval = BillingInterval.Monthly, PriceMonthly = 49m, PriceYearly = 490m };
    _db.Plans.Add(plan);
    await _db.SaveChangesAsync();

    DateTime lastMonthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-1);

    _db.Subscriptions.Add(new Subscription
    {
        StudioId         = studio.Id,
        PlanId           = plan.Id,
        Status           = SubscriptionStatus.Active,
        CreatedAt        = lastMonthStart.AddDays(5),        // created last month
        CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),      // still active this month
        TrialExpiresAt   = DateTime.UtcNow.AddDays(30),
    });
    await _db.SaveChangesAsync();
    _db.ChangeTracker.Clear();

    PlatformStatsResponse result = await CreateSut().Handle(new GetPlatformStatsQuery(), default);

    // MRR = 49, last month MRR = 49 → growth = 0 (no change).
    // Both this month and last month have the same subscription.
    result.Mrr.Should().Be(49m);
    result.MrrGrowthPercent.Should().Be(0.0); // flat, not positive
}
```

> Note: testing a positive `MrrGrowthPercent` requires seeding a subscription
> that did NOT exist last month (created this month). The negative case is
> when `lastMonthMrr` is 0 and `mrr` > 0, which yields `mrrGrowthPercent = 100`.

Run `dotnet test` — all tests must pass.

**Commit:** `test(platform): add GetPlatformStats tests for PastDue, Suspended, bucket-sum invariant`

---

## 11. Tests — Frontend

### 11a. Update `IssuerDashboardPage.test.tsx`

**File:** `frontend/src/features/platform/__tests__/IssuerDashboardPage.test.tsx`

1. **Update `STATS` seed object** — add the new fields with reasonable values:
   ```typescript
   const STATS: PlatformStatsResponse = {
     totalStudios:        12,
     activeSubscriptions: 8,
     trialStudios:        3,
     gracePeriodStudios:  1,
     pastDueStudios:      0,     // new
     cancelledStudios:    0,     // new
     suspendedStudios:    0,     // new
     mrr:                 392,
     mrrGrowthPercent:    12.5,  // new
     trialConversionRate: 0.727,
     newStudiosThisMonth: 2,
   };
   ```

2. **Add MSW handler for `mrr-history`** — without it the test will throw
   an unhandled request error once `MrrChart` mounts:
   ```typescript
   const server = setupServer(
     http.get("http://localhost/api/v1/platform/stats", () =>
       HttpResponse.json(STATS),
     ),
     http.get("http://localhost/api/v1/platform/subscriptions", () =>
       HttpResponse.json(SUBSCRIPTIONS),
     ),
     http.get("http://localhost/api/v1/platform/mrr-history", () =>
       HttpResponse.json([]),   // empty — MrrChart renders gracefully with no data
     ),
   );
   ```

3. **Replace the "quick nav links" test** — quick nav is removed, so the
   test that checks for "Plans", "Referrals", "Reports" links must be
   replaced with tests for the new KPI-driven navigation:
   ```typescript
   it("KPI card 'Active Subscriptions' links to subscriptions filtered by Active", async () => {
     renderPage();
     await screen.findByText("8"); // active subscriptions value
     const link = screen.getByRole("link", { name: /active subscriptions/i });
     expect(link).toHaveAttribute("href", "/platform/subscriptions?status=Active");
   });

   it("KPI card 'Past Due' links to subscriptions filtered by PastDue", async () => {
     renderPage();
     await screen.findByText("8");
     const link = screen.getByRole("link", { name: /past due/i });
     expect(link).toHaveAttribute("href", "/platform/subscriptions?status=PastDue");
   });
   ```

4. **Add test for MRR growth subtitle:**
   ```typescript
   it("shows MRR growth percentage in the MRR card subtitle", async () => {
     renderPage();
     expect(await screen.findByText(/\+12\.5% vs last month/i)).toBeInTheDocument();
   });
   ```

5. **Add test for at-risk urgency:**
   ```typescript
   it("shows 'Payment overdue' label for PastDue studios in at-risk widget", async () => {
     renderPage();
     expect(await screen.findByText("Payment overdue")).toBeInTheDocument();
   });
   ```

6. **Add test for at-risk count badge:**
   ```typescript
   it("shows count badge on At-Risk section title", async () => {
     renderPage();
     await screen.findByText("GracePeriod Studio");
     // atRisk.length = 2 (one GracePeriod + one PastDue)
     expect(screen.getByText("2")).toBeInTheDocument();
   });
   ```

### 11b. Update `IssuerLayout.test.tsx`

**File:** `frontend/src/layouts/__tests__/IssuerLayout.test.tsx`

Read this file first. Add or update tests to verify:
- "Notifications" text link is NOT in the nav (only the bell icon component remains)
- "Log out" button does NOT appear at the top level of the header as an always-visible button
- Clicking the user menu area reveals a "Log out" option

```typescript
it("does not render a 'Notifications' text link in the nav", () => {
  renderLayout();
  // NotificationBell renders a bell icon, not a "Notifications" text link
  expect(screen.queryByRole("link", { name: /^notifications$/i })).not.toBeInTheDocument();
});

it("does not show Log out as a persistent top-level button", () => {
  renderLayout();
  expect(screen.queryByRole("button", { name: /log out/i })).not.toBeInTheDocument();
});

it("reveals Log out inside the user menu dropdown on click", async () => {
  renderLayout();
  const userMenu = screen.getByRole("button", { expanded: false });
  await userEvent.click(userMenu);
  expect(await screen.findByRole("button", { name: /log out/i })).toBeInTheDocument();
});
```

> Note: the `renderLayout` helper in the existing test file may already set up
> the Redux provider and MemoryRouter. Follow its pattern — do not duplicate setup.

Run `pnpm test` — all tests must pass.

**Commit:** `test(dashboard): update tests for new KPI fields, quick nav removal, urgency labels`

---

## 12. Final Verification

1. `dotnet build` — zero errors.
2. `dotnet test` — all tests pass.
3. `pnpm --dir frontend lint` — zero errors.
4. `pnpm --dir frontend test` — all tests pass.
5. Verify the bucket-sum invariant manually:
   `grep -n "TotalStudios" Pena_e_Arte.Application/Platform/Queries/GetPlatformStatsQuery.cs`
   Confirm `totalStudios` is `active.Count` (not `studios.Count`), and that
   `active` is filtered by `IsActive == true`.
6. Verify no `IgnoreQueryFilters()` was added to any file that wasn't
   already in the approved table.
7. Verify no new npm or NuGet packages were added:
   `git diff --name-only | grep -E "(package\.json|\.csproj)"`
   These files may have changed only due to script additions — confirm no
   `dependencies` or `PackageReference` lines were added.
8. `git log --oneline -15` — confirm all commits from this session are present.

---

## Reference: Issue → Task Map

| Issue | Task |
|---|---|
| #1 KPI math bug | Task 1 |
| #2 New This Month caveat | Task 6d |
| #3 No expiry urgency | Task 6h |
| #4 Duplicate studio name | Task 6j |
| #5 Duplicate Notifications | Task 4a |
| #6 Redundant quick nav | Task 6f |
| #7 KPI cards not interactive | Task 6e (filtered hrefs) + Task 8 |
| #8 Log out top-level | Task 4b |
| #9 Chart 92% empty | Task 7a |
| #10 No tooltip | Task 7b |
| #11 MRR arrow no % | Task 6e (subtitle with growth %) |
| #12 No Y-axis label | Task 7c |
| #13 No Past Due KPI | Task 1 + Task 6e |
| #14 No Churn metric | Task 1 (cancelledStudios) + Task 6e |
| #15 No ARPU | Task 6e (ARPU = mrr ÷ active) |
| #16 No Suspended count | Task 1 + Task 6e |
| #17 No inline actions on at-risk | Task 6h |
| #18 No count badge on at-risk title | Task 6i |
| #19 At-Risk only GracePeriod | Already correct in code (`AT_RISK_STATUSES` includes PastDue) — verify backend returns PastDue studios in the subscriptions list |
| #20 Currency format | Task 6a |
| #21 Icon inconsistency | Task 6c + Task 6e (accent colors) |
| #22 No period indicator on KPIs | Task 6b (subtitle prop) + Task 6e |
