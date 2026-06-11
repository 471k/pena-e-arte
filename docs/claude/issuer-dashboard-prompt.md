# Issuer Dashboard & Platform Features — Overnight Execution Prompt

> Self-contained implementation spec. Read `CLAUDE.md`, `docs/claude/backend.md`,
> `docs/claude/frontend.md`, `docs/claude/database.md`, and `docs/claude/conventions.md`
> before starting. Follow the "Adding a New Feature" checklist in `architecture.md`
> for every feature below.
>
> Execute features in the exact order listed. Each section is a complete unit —
> finish all layers (Domain → Application → Infrastructure → API → Frontend → Tests)
> before moving to the next feature.

---

## Scope Summary

Five features, one bug fix, and one refactor:

| # | Feature | Type |
|---|---|---|
| F1 | Platform Statistics API + KPI widgets | New |
| F2 | Subscription Oversight (list + trial extension) | New |
| F3 | Platform Referral Code Management | New |
| F4 | Industry Reports Viewer (frontend only) | New |
| F5 | Issuer Dashboard Page (home screen) | New |
| F6 | `AllowBrandingRemoval` on UpdatePlan | Completion |
| FIX | Remove duplicate plan CRUD in IssuerEndpoints.cs | Bug fix / refactor |

---

## Global Rules (enforced on every file)

- Every new backend endpoint → `RequireAuthorization("IssuerOnly")` unless noted.
- Every new DB query on tenant data → goes through EF Core global query filters, OR uses
  `IgnoreQueryFilters()` only where explicitly permitted below (platform stats and
  subscription oversight are issuer-scoped and approved to bypass filters).
- No PII in logs. Every log includes `tenant_id` where applicable; for issuer-level
  aggregate queries use `"platform"` as the tenant context in log enrichment.
- No business logic in endpoints — MediatR only.
- TypeScript strict mode. No `any`. Named exports only. No `useEffect` for data fetching.
- Write tests alongside every handler and every meaningful component.

---

## FIX — Remove Duplicate Plan CRUD from IssuerEndpoints.cs

**Problem:** `IssuerEndpoints.cs` at `/api/v1/plans` and `BillingEndpoints.cs` at
`/api/v1/billing/plans` both register `GetPlansQuery`, `CreatePlanCommand`,
`UpdatePlanCommand`, and `DeletePlanCommand`. The canonical path is `/api/v1/billing/plans`
(already used by the frontend's `billingApi.ts`).

**Fix:**

Delete `Pena_e_Arte.API/Endpoints/IssuerEndpoints.cs` entirely.

Verify in `Program.cs` that `app.MapIssuerEndpoints()` is removed after deletion.

No handler, contract, or frontend change needed — the billing path was already wired.

---

## F6 — AllowBrandingRemoval on UpdatePlan

**Why:** The `Plan` entity has `AllowBrandingRemoval` (bool) per the architecture, but
`UpdatePlanRequest`, `UpdatePlanCommand`, and `UpdatePlanHandler` never expose it.
The issuer currently cannot toggle which plans unlock branding removal.

### Contracts

Update `Pena_e_Arte.Contracts/Requests/UpdatePlanRequest.cs`:

```csharp
public record UpdatePlanRequest(
    string Name,
    decimal PriceMonthly,
    decimal PriceYearly,
    int     YearlyDiscountPercent,
    bool    AllowBrandingRemoval);
```

### Application

Update `UpdatePlanCommand` record to include `AllowBrandingRemoval`.

Update `UpdatePlanHandler` to set `plan.AllowBrandingRemoval = command.Request.AllowBrandingRemoval`.

Update `UpdatePlanValidator` to add no new rules (bool is always valid, but add a rule
confirming `YearlyDiscountPercent` is between 0 and 100 if not already present).

### Frontend

In `features/billing/billingApi.ts`, update `UpdatePlanRequest` interface:

```typescript
export interface UpdatePlanRequest {
  name:                  string;
  priceMonthly:          number;
  priceYearly:           number;
  yearlyDiscountPercent: number;
  allowBrandingRemoval:  boolean;
}
```

In `features/platform/components/PlanManagementPage.tsx`, add a checkbox to the
`PlanForm` for `allowBrandingRemoval`:

```typescript
// Add to schema:
allowBrandingRemoval: z.boolean(),

// Add to form JSX after yearlyDiscountPercent field:
<div className="flex items-center gap-2 pt-1">
  <input
    type="checkbox"
    id="allowBrandingRemoval"
    {...register("allowBrandingRemoval")}
    className="h-4 w-4 rounded border-input"
  />
  <Label htmlFor="allowBrandingRemoval" className="text-sm font-normal cursor-pointer">
    Allow branding removal on this plan
  </Label>
</div>
```

Also update `PlanCard` to show a badge when `allowBrandingRemoval` is true:

```typescript
// In plan.name/billingInterval row, add:
{plan.allowBrandingRemoval && (
  <span className="text-xs bg-muted text-muted-foreground px-1.5 py-0.5 rounded">
    no-branding
  </span>
)}
```

Update `PlanResponse` type in `features/billing/billing.types.ts` to include
`allowBrandingRemoval: boolean`.

### Tests

Add a unit test: `UpdatePlan_WithAllowBrandingRemoval_SetsFlag`.

---

## F1 — Platform Statistics API + KPI Widgets

### Goal

Single endpoint returning a platform-wide snapshot. Used by the issuer dashboard.
Query uses `IgnoreQueryFilters()` — this is the fourth approved use (see architecture.md).

### Domain

No new entity. Query reads existing `Studio`, `Subscription`, `Plan` entities.

### Application

**Create** `Pena_e_Arte.Application/Platform/Queries/GetPlatformStatsQuery.cs`:

```csharp
public record GetPlatformStatsQuery : IRequest<PlatformStatsResponse>;

public class GetPlatformStatsHandler(AppDbContext db)
    : IRequestHandler<GetPlatformStatsQuery, PlatformStatsResponse>
{
    public async Task<PlatformStatsResponse> Handle(
        GetPlatformStatsQuery _, CancellationToken ct)
    {
        // All queries here use IgnoreQueryFilters() — issuer-scoped, approved.
        List<Studio> studios = await db.Studios
            .IgnoreQueryFilters()
            .Include(s => s.Subscription)
                .ThenInclude(sub => sub!.Plan)
            .ToListAsync(ct);

        int total      = studios.Count;
        int active     = studios.Count(s => s.Subscription?.Status == SubscriptionStatus.Active);
        int trialing   = studios.Count(s => s.Subscription?.Status == SubscriptionStatus.Trialing
                                         || (s.Subscription == null && s.TrialExpiresAt > DateTime.UtcNow));
        int grace      = studios.Count(s => s.Subscription?.Status == SubscriptionStatus.GracePeriod);
        int pastDue    = studios.Count(s => s.Subscription?.Status == SubscriptionStatus.PastDue);
        int suspended  = studios.Count(s => s.IsSuspended);
        int atRisk     = grace + pastDue;

        // MRR: sum of active subscriptions' monthly plan price
        decimal mrr = studios
            .Where(s => s.Subscription?.Status == SubscriptionStatus.Active
                     && s.Subscription.Plan != null)
            .Sum(s => s.Subscription!.Plan!.PriceMonthly);

        // Trial-to-paid conversion rate (last 90 days)
        // Studios whose trial started in last 90 days that converted to active
        DateTime cutoff         = DateTime.UtcNow.AddDays(-90);
        int recentTrials        = studios.Count(s => s.CreatedAt >= cutoff);
        int recentConversions   = studios.Count(s => s.CreatedAt >= cutoff
                                                  && s.Subscription?.Status == SubscriptionStatus.Active);
        double conversionRate   = recentTrials > 0
            ? Math.Round((double)recentConversions / recentTrials * 100, 1)
            : 0;

        return new PlatformStatsResponse(
            TotalStudios:        total,
            ActiveStudios:       active,
            TrialingStudios:     trialing,
            GracePeriodStudios:  grace,
            PastDueStudios:      pastDue,
            SuspendedStudios:    suspended,
            AtRiskStudios:       atRisk,
            Mrr:                 mrr,
            TrialToPaidConversionRate: conversionRate);
    }
}
```

No FluentValidation needed (query has no inputs).

### Contracts

**Create** `Pena_e_Arte.Contracts/Responses/PlatformStatsResponse.cs`:

```csharp
public record PlatformStatsResponse(
    int     TotalStudios,
    int     ActiveStudios,
    int     TrialingStudios,
    int     GracePeriodStudios,
    int     PastDueStudios,
    int     SuspendedStudios,
    int     AtRiskStudios,
    decimal Mrr,
    double  TrialToPaidConversionRate);
```

### API

Add to `Pena_e_Arte.API/Endpoints/PlatformEndpoints.cs`:

```csharp
group.MapGet("stats", GetPlatformStats);

private static async Task<IResult> GetPlatformStats(
    ISender           mediator,
    CancellationToken ct)
{
    PlatformStatsResponse result = await mediator.Send(new GetPlatformStatsQuery(), ct);
    return Results.Ok(result);
}
```

### Frontend

**Create** `frontend/src/features/platform/platform.types.ts`:

```typescript
export interface PlatformStatsResponse {
  totalStudios:              number;
  activeStudios:             number;
  trialingStudios:           number;
  gracePeriodStudios:        number;
  pastDueStudios:            number;
  suspendedStudios:          number;
  atRiskStudios:             number;
  mrr:                       number;
  trialToPaidConversionRate: number;
}

export interface PlatformSubscriptionResponse {
  studioId:         string;
  studioName:       string;
  planName:         string | null;
  status:           string;
  trialExpiresAt:   string;
  gracePeriodEnd:   string | null;
  currentPeriodEnd: string | null;
}

export interface PlatformReferralCodeResponse {
  id:              string;
  code:            string;
  studioId:        string;
  studioName:      string;
  isActive:        boolean;
  redemptionCount: number;
  expiresAt:       string | null;
  createdAt:       string;
}

export interface IndustryReportSummaryResponse {
  year:      number;
  month:     number;
  reportUrl: string;
}
```

**Create** `frontend/src/features/platform/platformApi.ts`:

```typescript
import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  PlatformStatsResponse,
  PlatformSubscriptionResponse,
  PlatformReferralCodeResponse,
  IndustryReportSummaryResponse,
} from "./platform.types";

export const platformApi = createApi({
  reducerPath: "platformApi",
  baseQuery,
  tagTypes: ["PlatformStats", "PlatformSubscription", "PlatformReferral", "IndustryReport"],
  endpoints: (builder) => ({
    getPlatformStats: builder.query<PlatformStatsResponse, void>({
      query: () => "platform/stats",
      providesTags: ["PlatformStats"],
    }),
    getPlatformSubscriptions: builder.query<PlatformSubscriptionResponse[], void>({
      query: () => "platform/subscriptions",
      providesTags: ["PlatformSubscription"],
    }),
    extendTrial: builder.mutation<void, { studioId: string; extensionDays: number }>({
      query: ({ studioId, extensionDays }) => ({
        url:    `platform/subscriptions/${studioId}/trial`,
        method: "PATCH",
        body:   { extensionDays },
      }),
      invalidatesTags: ["PlatformSubscription", "PlatformStats"],
    }),
    getIndustryReports: builder.query<IndustryReportSummaryResponse[], void>({
      query: () => "platform/reports/industry",
      providesTags: ["IndustryReport"],
    }),
    getPlatformReferralCodes: builder.query<PlatformReferralCodeResponse[], void>({
      query: () => "platform/referral-codes",
      providesTags: ["PlatformReferral"],
    }),
    deactivateReferralCode: builder.mutation<void, string>({
      query: (id) => ({ url: `platform/referral-codes/${id}/deactivate`, method: "PATCH" }),
      invalidatesTags: ["PlatformReferral"],
    }),
  }),
});

export const {
  useGetPlatformStatsQuery,
  useGetPlatformSubscriptionsQuery,
  useExtendTrialMutation,
  useGetIndustryReportsQuery,
  useGetPlatformReferralCodesQuery,
  useDeactivateReferralCodeMutation,
} = platformApi;
```

**Update** `frontend/src/app/store.ts` — add `platformApi` to reducer and middleware:

```typescript
import { platformApi } from "@/features/platform/platformApi";

// In reducer map add:
[platformApi.reducerPath]: platformApi.reducer,

// In middleware chain add:
.concat(platformApi.middleware)
```

**Create** `frontend/src/features/platform/index.ts` exporting the public API:

```typescript
export { platformApi } from "./platformApi";
export * from "./platform.types";
```

### Tests

**Create** `tests/Pena_e_Arte.IntegrationTests/Application/PlatformStatsIntegrationTests.cs`:

```csharp
public class PlatformStatsIntegrationTests(IntegrationTestFactory factory)
    : IClassFixture<IntegrationTestFactory>
{
    [Fact]
    public async Task GetPlatformStats_WithMixedStudios_ReturnsCorrectCounts()
    {
        // Arrange: seed studios in different states
        // Act: send GetPlatformStatsQuery
        // Assert: counts match seeded data, MRR sums active plan prices
    }

    [Fact]
    public async Task GetPlatformStats_NoStudios_ReturnsZeroMrr()
    {
        // Arrange: empty DB
        // Act / Assert: MRR == 0, conversion rate == 0
    }
}
```

---

## F2 — Subscription Oversight (List + Trial Extension)

### Goal

Issuer can see all subscriptions across tenants and extend a studio's trial.

### Application

**Create** `Pena_e_Arte.Application/Platform/Queries/GetPlatformSubscriptionsQuery.cs`:

```csharp
public record GetPlatformSubscriptionsQuery : IRequest<IReadOnlyList<PlatformSubscriptionResponse>>;

public class GetPlatformSubscriptionsHandler(AppDbContext db)
    : IRequestHandler<GetPlatformSubscriptionsQuery, IReadOnlyList<PlatformSubscriptionResponse>>
{
    public async Task<IReadOnlyList<PlatformSubscriptionResponse>> Handle(
        GetPlatformSubscriptionsQuery _, CancellationToken ct)
    {
        return await db.Studios
            .IgnoreQueryFilters()
            .Include(s => s.Subscription)
                .ThenInclude(sub => sub!.Plan)
            .OrderBy(s => s.Name)
            .Select(s => new PlatformSubscriptionResponse(
                s.Id,
                s.Name,
                s.Subscription != null ? s.Subscription.Plan!.Name : null,
                s.Subscription != null ? s.Subscription.Status.ToString() : "NoSubscription",
                s.TrialExpiresAt,
                s.Subscription != null ? s.Subscription.GracePeriodEnd : null,
                s.Subscription != null ? s.Subscription.CurrentPeriodEnd : null))
            .ToListAsync(ct);
    }
}
```

**Create** `Pena_e_Arte.Application/Platform/Commands/ExtendTrialCommand.cs`:

```csharp
public record ExtendTrialCommand(Guid StudioId, int ExtensionDays)
    : IRequest<Unit>;

public class ExtendTrialHandler(AppDbContext db)
    : IRequestHandler<ExtendTrialCommand, Unit>
{
    public async Task<Unit> Handle(ExtendTrialCommand command, CancellationToken ct)
    {
        Studio studio = await db.Studios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.Id == command.StudioId, ct)
            ?? throw new StudioNotFoundException(command.StudioId);

        studio.TrialExpiresAt = studio.TrialExpiresAt > DateTime.UtcNow
            ? studio.TrialExpiresAt.AddDays(command.ExtensionDays)
            : DateTime.UtcNow.AddDays(command.ExtensionDays);

        // If subscription is in grace period and trial is being extended, revert to trialing
        if (studio.Subscription is { Status: SubscriptionStatus.GracePeriod })
            studio.Subscription.Status = SubscriptionStatus.Trialing;

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
```

**Create** `Pena_e_Arte.Application/Platform/Validators/ExtendTrialValidator.cs`:

```csharp
public class ExtendTrialValidator : AbstractValidator<ExtendTrialCommand>
{
    public ExtendTrialValidator()
    {
        RuleFor(x => x.StudioId).NotEmpty();
        RuleFor(x => x.ExtensionDays).InclusiveBetween(1, 90)
            .WithMessage("Extension must be between 1 and 90 days.");
    }
}
```

### Contracts

**Create** `Pena_e_Arte.Contracts/Responses/PlatformSubscriptionResponse.cs`:

```csharp
public record PlatformSubscriptionResponse(
    Guid      StudioId,
    string    StudioName,
    string?   PlanName,
    string    Status,
    DateTime  TrialExpiresAt,
    DateTime? GracePeriodEnd,
    DateTime? CurrentPeriodEnd);
```

**Create** `Pena_e_Arte.Contracts/Requests/ExtendTrialRequest.cs`:

```csharp
public record ExtendTrialRequest(int ExtensionDays);
```

### API

Add to `PlatformEndpoints.cs`:

```csharp
group.MapGet("subscriptions",                          GetPlatformSubscriptions);
group.MapPatch("subscriptions/{studioId:guid}/trial",  ExtendTrial);

private static async Task<IResult> GetPlatformSubscriptions(
    ISender           mediator,
    CancellationToken ct)
{
    IReadOnlyList<PlatformSubscriptionResponse> result =
        await mediator.Send(new GetPlatformSubscriptionsQuery(), ct);
    return Results.Ok(result);
}

private static async Task<IResult> ExtendTrial(
    Guid               studioId,
    ExtendTrialRequest request,
    ISender            mediator,
    CancellationToken  ct)
{
    await mediator.Send(new ExtendTrialCommand(studioId, request.ExtensionDays), ct);
    return Results.NoContent();
}
```

### Frontend

**Create** `frontend/src/features/platform/components/SubscriptionOversightPage.tsx`:

The page renders a filterable list of all platform subscriptions. Full spec:

- Header: "Subscriptions" with a status filter (All | Active | Trialing | Grace Period | Past Due | Suspended | No Subscription).
- Each row is a `Card` showing: studio name, plan name (or "No plan"), status badge, trial expiry or period end date.
- Status badge colors: Active → green, Trialing → blue, GracePeriod → amber, PastDue → red, Suspended → slate, NoSubscription → muted.
- Each row has an "Extend trial" button that opens an inline form with a number input (1–90 days) and a confirm button. On submit calls `useExtendTrialMutation`. On success invalidates and shows updated state.
- At-risk rows (GracePeriod, PastDue) are sorted to the top.
- Imports: `useGetPlatformSubscriptionsQuery`, `useExtendTrialMutation` from `platformApi`.
- No Redux slice — RTK Query only.
- Named export: `SubscriptionOversightPage`.

### Tests

```csharp
// ExtendTrialCommand tests:
// ExtendTrial_ActiveTrial_ExtendsByDays
// ExtendTrial_ExpiredTrial_SetsNewTrialFromNow
// ExtendTrial_GracePeriodStudio_RevertsToTrialing
// ExtendTrial_ExtensionDaysOutOfRange_ThrowsValidationException
// ExtendTrial_StudioNotFound_ThrowsStudioNotFoundException
```

---

## F3 — Platform Referral Code Management

### Goal

Issuer can view all referral codes across all studios and deactivate any code.

### Application

**Create** `Pena_e_Arte.Application/Platform/Queries/GetPlatformReferralCodesQuery.cs`:

```csharp
public record GetPlatformReferralCodesQuery
    : IRequest<IReadOnlyList<PlatformReferralCodeResponse>>;

public class GetPlatformReferralCodesHandler(AppDbContext db)
    : IRequestHandler<GetPlatformReferralCodesQuery, IReadOnlyList<PlatformReferralCodeResponse>>
{
    public async Task<IReadOnlyList<PlatformReferralCodeResponse>> Handle(
        GetPlatformReferralCodesQuery _, CancellationToken ct)
    {
        return await db.ReferralCodes
            .IgnoreQueryFilters()
            .Include(r => r.Studio)
            .Include(r => r.Redemptions)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new PlatformReferralCodeResponse(
                r.Id,
                r.Code,
                r.StudioId,
                r.Studio.Name,
                r.IsActive,
                r.Redemptions.Count,
                r.ExpiresAt,
                r.CreatedAt))
            .ToListAsync(ct);
    }
}
```

**Create** `Pena_e_Arte.Application/Platform/Commands/DeactivateReferralCodeCommand.cs`:

```csharp
public record DeactivateReferralCodeCommand(Guid ReferralCodeId) : IRequest<Unit>;

public class DeactivateReferralCodeHandler(AppDbContext db)
    : IRequestHandler<DeactivateReferralCodeCommand, Unit>
{
    public async Task<Unit> Handle(DeactivateReferralCodeCommand command, CancellationToken ct)
    {
        ReferralCode code = await db.ReferralCodes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == command.ReferralCodeId, ct)
            ?? throw new ReferralCodeNotFoundException(command.ReferralCodeId);

        code.IsActive = false;
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
```

**Create** `Pena_e_Arte.Application/Platform/Validators/DeactivateReferralCodeValidator.cs`:

```csharp
public class DeactivateReferralCodeValidator : AbstractValidator<DeactivateReferralCodeCommand>
{
    public DeactivateReferralCodeValidator()
    {
        RuleFor(x => x.ReferralCodeId).NotEmpty();
    }
}
```

Add `ReferralCodeNotFoundException` to `Domain/Exceptions/` if not already present:

```csharp
public class ReferralCodeNotFoundException(Guid id)
    : DomainException($"Referral code {id} was not found.");
```

### Contracts

**Create** `Pena_e_Arte.Contracts/Responses/PlatformReferralCodeResponse.cs`:

```csharp
public record PlatformReferralCodeResponse(
    Guid      Id,
    string    Code,
    Guid      StudioId,
    string    StudioName,
    bool      IsActive,
    int       RedemptionCount,
    DateTime? ExpiresAt,
    DateTime  CreatedAt);
```

### API

Add to `PlatformEndpoints.cs`:

```csharp
group.MapGet("referral-codes",                          GetPlatformReferralCodes);
group.MapPatch("referral-codes/{id:guid}/deactivate",   DeactivateReferralCode);

private static async Task<IResult> GetPlatformReferralCodes(
    ISender           mediator,
    CancellationToken ct)
{
    IReadOnlyList<PlatformReferralCodeResponse> result =
        await mediator.Send(new GetPlatformReferralCodesQuery(), ct);
    return Results.Ok(result);
}

private static async Task<IResult> DeactivateReferralCode(
    Guid              id,
    ISender           mediator,
    CancellationToken ct)
{
    await mediator.Send(new DeactivateReferralCodeCommand(id), ct);
    return Results.NoContent();
}
```

### Frontend

**Create** `frontend/src/features/platform/components/PlatformReferralPage.tsx`:

Full spec:

- Header: "Referral Codes" with a count badge.
- Filter toggle: All | Active only | Inactive only.
- Each row is a `Card` showing: code (monospaced font), studio name, redemption count badge, expiry date (or "No expiry"), created date, active/inactive status badge.
- Inactive rows are visually muted (`opacity-60`).
- Each active row has a "Deactivate" button with a two-step confirm (same pattern as `IssuerStudioListPage`).
- On deactivate, calls `useDeactivateReferralCodeMutation`. On success row updates to inactive state immediately via RTK Query cache invalidation.
- Named export: `PlatformReferralPage`.

### Tests

```csharp
// DeactivateReferralCode_ActiveCode_SetsIsActiveFalse
// DeactivateReferralCode_AlreadyInactive_StillSucceeds (idempotent)
// DeactivateReferralCode_NotFound_ThrowsReferralCodeNotFoundException
// GetPlatformReferralCodes_ReturnsAllTenantsCodesWithRedemptionCount
```

---

## F4 — Industry Reports Viewer (Frontend Only)

The backend endpoint `GET /api/v1/platform/reports/industry` already exists in
`PlatformEndpoints.cs`. Only the frontend page is missing.

**Create** `frontend/src/features/platform/components/IndustryReportsPage.tsx`:

Full spec:

- Header: "Industry Reports" with a description subtitle: "Monthly anonymised platform-wide analytics."
- Data source: `useGetIndustryReportsQuery()` from `platformApi`.
- Renders a list of report cards, one per available month, sorted newest first.
- Each card shows: "Month Year" title (e.g. "May 2026"), a "Download report" link that opens `reportUrl` in a new tab (`target="_blank" rel="noopener noreferrer"`).
- Empty state: "No reports available yet. The first report generates on the 1st of next month."
- Loading and error states following the same pattern as `IssuerStudioListPage`.
- Named export: `IndustryReportsPage`.

No backend, no new tests needed (endpoint already tested in `IndustryReportsIntegrationTests.cs`).

---

## F5 — Issuer Dashboard Page

### Goal

Replace the owner-focused `DashboardPage` for the issuer role with a platform-admin home
screen. The issuer's entry point after login is `/platform` → `IssuerLayout` →
`IssuerDashboardPage`.

### Frontend

**Create** `frontend/src/features/platform/components/IssuerDashboardPage.tsx`:

#### Structure

```
IssuerDashboardPage
├── Header ("Platform Dashboard" + today's date)
├── KpiSection       — stat cards grid
├── AtRiskSection    — studios needing attention
└── QuickNav         — grid of links to platform sections
```

#### KpiSection

Six `StatCard` components in a 2-column grid (3 rows):

| Card | Value | Label | Color hint |
|---|---|---|---|
| 1 | `totalStudios` | Total Studios | neutral |
| 2 | `activeStudios` | Active | green |
| 3 | `trialingStudios` | Trialing | blue |
| 4 | `mrr` formatted as currency | MRR | neutral |
| 5 | `trialToPaidConversionRate` + "%" | Conversion (90d) | neutral |
| 6 | `atRiskStudios` | At Risk | amber if > 0, neutral if 0 |

`StatCard` is a local component (not shared) — a `Card` with a large number, a label below it, and an optional colored dot indicator.

Data source: `useGetPlatformStatsQuery()`.

Show skeleton placeholders (grey rounded rectangles, same dimensions as the cards) while loading.

#### AtRiskSection

Visible only when `atRiskStudios > 0`.

Title: "Needs Attention" with an amber `AlertTriangle` icon.

Data source: `useGetPlatformSubscriptionsQuery()`.

Filter to studios where `status === "GracePeriod" || status === "PastDue"`.

Each at-risk studio renders as a compact row showing:
- Studio name
- Status badge (amber for GracePeriod, red for PastDue)
- Trial expiry or grace period end date
- "View" button that navigates to `/platform/subscriptions`

Limit to 5 rows maximum; if more, show "+ N more → Subscriptions" link.

#### QuickNav

Five nav tiles in a 3-column grid (with last row left-aligned):

| Label | Icon (lucide) | Route |
|---|---|---|
| Studios | `Building2` | `/platform/studios` |
| Plans | `CreditCard` | `/platform/plans` |
| Subscriptions | `Receipt` | `/platform/subscriptions` |
| Referrals | `Share2` | `/platform/referrals` |
| Reports | `BarChart3` | `/platform/reports` |

Same tile style as `DashboardPage`'s `QuickNav`.

Named export: `IssuerDashboardPage`.

### Router Update

Update `frontend/src/app/router.tsx`:

The issuer role must be routed to `IssuerLayout` which already exists. Add the following
child routes under the `/platform` path group:

```typescript
{ index: true,            element: <IssuerDashboardPage /> },
{ path: "studios",        element: <IssuerStudioListPage /> },
{ path: "plans",          element: <PlanManagementPage /> },
{ path: "subscriptions",  element: <SubscriptionOversightPage /> },
{ path: "referrals",      element: <PlatformReferralPage /> },
{ path: "reports",        element: <IndustryReportsPage /> },
```

Ensure the `RoleGuard` wrapping `/platform` allows only `"issuer"`.

After login, the router already redirects users by role. Confirm the issuer redirect
target is `/platform` (not `/dashboard`). If it currently redirects to `/dashboard`,
update the role-redirect logic.

### IssuerLayout Nav Update

Update `frontend/src/layouts/IssuerLayout.tsx` to include nav links for all five platform
sections:

```typescript
const NAV_ITEMS = [
  { label: "Dashboard",      href: "/platform",               icon: <LayoutDashboard /> },
  { label: "Studios",        href: "/platform/studios",       icon: <Building2 /> },
  { label: "Plans",          href: "/platform/plans",         icon: <CreditCard /> },
  { label: "Subscriptions",  href: "/platform/subscriptions", icon: <Receipt /> },
  { label: "Referrals",      href: "/platform/referrals",     icon: <Share2 /> },
  { label: "Reports",        href: "/platform/reports",       icon: <BarChart3 /> },
];
```

Use `NavLink` from React Router for active state highlighting.

### Tests

Frontend component tests in `frontend/src/features/platform/__tests__/`:

**`IssuerDashboardPage.test.tsx`:**
```typescript
// renders KPI cards with data from getPlatformStats
// renders skeleton cards while loading
// renders AtRiskSection only when atRiskStudios > 0
// does not render AtRiskSection when atRiskStudios === 0
// QuickNav tiles navigate to correct routes
```

**`SubscriptionOversightPage.test.tsx`:**
```typescript
// renders all subscriptions from getPlatformSubscriptions
// filters by status when filter selected
// extend trial form submits with correct studioId and days
// at-risk rows sorted to top
```

---

## Final Checklist

Before committing, verify:

- [ ] `IssuerEndpoints.cs` is deleted and `MapIssuerEndpoints()` removed from `Program.cs`.
- [ ] `UpdatePlanRequest` includes `AllowBrandingRemoval` and the handler sets it.
- [ ] `PlatformEndpoints.cs` registers all five new routes under `IssuerOnly`.
- [ ] `IgnoreQueryFilters()` usage in F1, F2, F3 is documented with inline comments referencing the architecture approved-usage list (4th, 5th, 6th usage respectively — update `architecture.md`).
- [ ] `platformApi` added to `store.ts` reducer and middleware.
- [ ] Router routes issuer to `/platform` not `/dashboard`.
- [ ] `IssuerLayout` nav includes all five sections.
- [ ] All new integration tests pass with `dotnet test`.
- [ ] All new frontend tests pass with `pnpm test`.
- [ ] No new npm/NuGet packages introduced.
- [ ] No PII in any new log statements.
- [ ] `architecture.md` Decisions Log updated with the `IgnoreQueryFilters()` approved usages 4–6.
