# Overnight Prompt — Issuer Role: Autonomous QA → Bug Fix → Polish Loop
**Date:** 2026-07-01
**Mode:** Fully autonomous. No user present. Run until every loop exits clean.

---

## Your Mission

You are the platform's first real QA engineer. Your job has two phases that run in
sequence. Do not skip ahead to Phase 2 until Phase 1 exits with a green test suite.

**Phase 1 — Bug Hunt:** Walk the entire issuer section of the codebase as a user would,
layer by layer. Every bug you find gets fixed immediately, re-tested, and fixed again
if it still fails — until that specific item is green. Then move to the next item.

**Phase 2 — Polish:** After all bugs are gone, decide what a finished platform admin
section of a professional SaaS product needs, then implement each missing piece
systematically until the issuer role feels complete.

---

## Constraints (identical to every other overnight prompt)

- No new npm or NuGet packages.
- No `useEffect` for data fetching. Approved: resize events, keyboard events,
  outside-click detection, browser API side-effects.
- TypeScript strict mode. No `any`. No default exports on components.
- No business logic in endpoints — endpoints call MediatR only.
- Every DB query on tenant data through EF Core global query filters.
  Only `issuer` role may call `IgnoreQueryFilters()`.
- Every endpoint must have `.RequireAuthorization()` with the correct policy.
- Never log PII. All Serilog logs must include `tenant_id`, `user_id`, `request_id`.
- No secrets in source.

---

## Required Reading (do before touching any file)

```
CLAUDE.md
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/database.md
docs/claude/architecture.md
docs/claude/conventions.md
```

---

## Issuer Surface Map

The issuer role routes entirely under `/platform` with `IssuerLayout`. Know every
screen and its purpose before auditing them:

| Route | Component | Purpose |
|---|---|---|
| `/platform` | `IssuerDashboardPage` | KPI grid, MRR chart, at-risk studios |
| `/platform/studios` | `IssuerStudioListPage` | All tenants: suspend/trial/activate/cancel |
| `/platform/studios/:id` | `IssuerStudioDetailPage` | Single studio admin detail |
| `/platform/plans` | `PlanManagementPage` | Plan CRUD, branding gate |
| `/platform/subscriptions` | `SubscriptionOversightPage` | All subscriptions, status filters |
| `/platform/referrals` | `PlatformReferralPage` | Referral code management |
| `/platform/reports` | `IndustryReportsPage` | Trigger + download monthly reports |

Backend issuer API group (all `IssuerOnly`):
```
GET    /api/v1/platform/stats
GET    /api/v1/platform/mrr-history[?months=N]
GET    /api/v1/platform/subscriptions
PATCH  /api/v1/platform/subscriptions/{studioId}/trial
PATCH  /api/v1/platform/subscriptions/{studioId}/cancel
POST   /api/v1/platform/studios/{studioId}/subscription/activate
GET    /api/v1/platform/studios/{studioId}
POST   /api/v1/platform/studios/{studioId}/referral-codes
GET    /api/v1/platform/referral-codes
PATCH  /api/v1/platform/referral-codes/{id}/deactivate
PATCH  /api/v1/platform/referral-codes/{id}/reactivate
DELETE /api/v1/platform/referral-codes/{id}
GET    /api/v1/platform/reports/industry
POST   /api/v1/platform/reports/industry/trigger
GET    /api/v1/billing/plans        (also used by owner; issuer sees all)
POST   /api/v1/billing/plans
PUT    /api/v1/billing/plans/{id}
DELETE /api/v1/billing/plans/{id}
GET    /api/v1/studios              (issuer sees all tenants)
POST   /api/v1/studios/{id}/suspend
POST   /api/v1/studios/{id}/unsuspend
```

Frontend files in scope:
```
frontend/src/
  layouts/IssuerLayout.tsx
  layouts/__tests__/IssuerLayout.test.tsx
  features/platform/
    platformApi.ts
    platform.types.ts
    index.ts
    components/
      IssuerDashboardPage.tsx
      IssuerStudioListPage.tsx
      IssuerStudioDetailPage.tsx
      PlanManagementPage.tsx
      SubscriptionOversightPage.tsx
      PlatformReferralPage.tsx
      IndustryReportsPage.tsx
      IndustryReportsPanel.tsx
      MrrChart.tsx
    __tests__/
      IssuerDashboardPage.test.tsx
      IssuerStudioListPage.test.tsx
      IssuerStudioDetailPage.test.tsx
      PlanManagementPage.test.tsx
      SubscriptionOversightPage.test.tsx
      PlatformReferralPage.test.tsx
      IndustryReportsPage.test.tsx
Backend files in scope:
  Pena_e_Arte.Application/Platform/
  Pena_e_Arte.API/Endpoints/Platform/
  Pena_e_Arte.Infrastructure/Persistence/Configurations/
  Pena_e_Arte.Domain/Entities/{Plan,Subscription,ReferralCode,...}.cs
  tests/Pena_e_Arte.UnitTests/
  tests/Pena_e_Arte.IntegrationTests/
```

---

# PHASE 1 — BUG HUNT

## The Loop Algorithm

```
LOOP:
  1. Run the full test suite:
       cd "Pena e Arte" && dotnet test --no-build
       cd frontend && pnpm test
  2. Collect every failing test.
  3. For each failure:
       a. Read the relevant source file(s) in full.
       b. Diagnose the root cause precisely.
       c. Fix exactly what is broken — nothing else.
       d. Run just that test file again to confirm the fix.
       e. If still failing: diagnose again from scratch, fix differently, re-run.
       f. Repeat until that test is green.
  4. After all individual fixes: run the full suite again.
  5. If any new failures appeared: go back to step 3.
  6. If suite is fully green: EXIT PHASE 1, ENTER PHASE 2.
```

## Audit Checklist (run WHILE fixing test failures — read these files proactively)

Work through each layer in order. For each item, read the file, identify bugs,
fix them, and write or update the corresponding test. Do not mark an item complete
until its test is green.

### Layer A — Backend Endpoints

#### A1. Authorization

For every endpoint under `/api/v1/platform/` and `/api/v1/billing/plans*`:
- Confirm `.RequireAuthorization("IssuerOnly")` is applied. No unprotected endpoint.
- Confirm no endpoint calls business logic directly — all go through MediatR.
- Confirm `IgnoreQueryFilters()` is ONLY in handlers listed in the architecture docs
  `IgnoreQueryFilters Approved Usages` table.
- Confirm the response type in the endpoint matches what the handler returns.

**Common bugs to look for:**
- Missing `.RequireAuthorization()` on a new endpoint.
- Handler projecting a field that doesn't exist on the domain entity.
- Handler reading `CurrentTenant.TenantId` instead of using `IgnoreQueryFilters()` on
  cross-tenant issuer queries.
- Missing `FluentValidation` validator for any command.

#### A2. Platform Stats

File: `Pena_e_Arte.Application/Platform/Queries/GetPlatformStatsQuery.cs`

Verify:
- `totalStudios` counts ALL studios (incl. suspended, incl. no subscription).
- `activeSubscriptions` only counts `Status == active`.
- `trialStudios` only counts `Status == trialing` AND `TrialExpiresAt > now`.
- `gracePeriodStudios` counts `Status == grace_period` OR
  (`TrialExpiresAt < now` AND `GracePeriodEnd > now` AND no active sub).
- `mrr` only sums active subscriptions.
- `mrrGrowthPercent` computes `(mrr_this_month - mrr_last_month) / mrr_last_month`.
  Handle divide-by-zero: return 0 if last month was 0.
- `trialConversionRate` = `active / (active + trial + grace_period)`. If denominator
  is 0, return 0.
- `newStudiosThisMonth` counts `CreatedAt >= first day of current month`.

**Fix any field that is computed incorrectly.**

#### A3. MRR History

File: `Pena_e_Arte.Application/Platform/Queries/GetMrrHistoryQuery.cs`

Verify:
- Returns `months` data points (default 12).
- Each point: `{ month: "YYYY-MM", mrr: decimal }`.
- Only counts `Status == active` subscriptions.
- Uses the plan's price at the time of the subscription, not the current plan price
  (which may have changed). If `Subscription.CurrentPriceMonthly` doesn't exist,
  use `Plan.PriceMonthly` as an approximation and document the limitation in a comment.
- Months with 0 active subscriptions return a data point with `mrr: 0` (not omitted).
- The response is ordered from oldest to newest month.

#### A4. Subscription Oversight

Files: `GetPlatformSubscriptionsHandler.cs`, `ExtendTrialHandler.cs`,
       `CancelSubscriptionHandler.cs`

Verify:
- `GetPlatformSubscriptionsHandler` returns ALL studios (not just those with a
  `Subscription` record). Studios without a subscription get `Status = "NoSubscription"`.
- `ExtendTrialHandler` validates `additionalDays` in [1, 90]. Returns 400 if out of range.
- `ExtendTrialHandler` extends `TrialExpiresAt` (not `GracePeriodEnd`). If trial is
  already expired, sets `TrialExpiresAt = now + additionalDays`.
- `CancelSubscriptionHandler` sets subscription Status to Cancelled. Also calls Stripe
  if a `StripeSubscriptionId` is present, then handles Stripe errors gracefully (log,
  don't rethrow — local cancellation must still succeed).
- `ActivateSubscriptionManuallyCommand` creates a `Subscription` row if none exists;
  updates status to `Active` if one already exists. Validates that `planId` exists.
  Does NOT call Stripe (this is a manual cash activation — Stripe uninvolved).

#### A5. Plans

Files: `CreatePlanCommand.cs`, `UpdatePlanCommand.cs`, `DeletePlanCommand.cs`,
       `GetPlansQuery.cs`

Verify:
- `CreatePlan`: validates `Name` is not empty, `PriceMonthly > 0`, `BillingInterval` is
  valid. Returns 409 if a plan with the same name already exists.
- `UpdatePlan`: `AllowBrandingRemoval` field is present in both command AND the
  `UpdatePlanRequest` contract. If missing, add it.
- `DeletePlan`: refuses to delete a plan that has active subscriptions (returns 409 with
  a clear message). Soft-delete preferred; if hard-delete, verify FK constraint handling.
- `GetPlans` (issuer): returns all plans including `AllowBrandingRemoval`.

#### A6. Referral Codes

Files: `GetPlatformReferralCodesHandler.cs`, `DeactivateReferralCodeHandler.cs`,
       `ReactivateReferralCodeHandler.cs`, `DeleteReferralCodeHandler.cs`,
       `IssuerGenerateReferralCodeHandler.cs`

Verify:
- `GetPlatformReferralCodes` returns all codes across all tenants (uses
  `IgnoreQueryFilters()`). Includes `studioName`, `code`, `isActive`, `isSingleUse`,
  `redemptionCount`, `expiresAt`.
- `DeactivateReferralCode` checks the code exists before deactivating. Returns 404 if not.
- `DeleteReferralCode` refuses to delete a code that has been redeemed
  (`redemptionCount > 0`). Returns 409 if redeemed.
- `IssuerGenerateReferralCode` validates the `studioId` exists. Generates an 8-char
  uppercase alphanumeric code. Verifies uniqueness (no collision). Returns 409 if the
  studio already has an active code of the same type.

#### A7. Industry Reports

Files: `GetIndustryReportsHandler.cs`, `TriggerIndustryReportHandler.cs`

Verify:
- `GetIndustryReports` returns a list ordered by most recent first.
  If no reports exist, returns empty array (not 404).
- `TriggerIndustryReport` enqueues a Hangfire job and returns 202 Accepted.
  Does NOT wait for the job to complete.
- The Hangfire job (`IndustryReportJob`) uses `IgnoreQueryFilters()` (approved usage #3)
  and writes anonymized JSON only. No PII in the report output.

#### A8. Studio Suspend / Unsuspend

Files: `SuspendStudioCommand.cs`, `UnsuspendStudioCommand.cs`

Verify:
- `SuspendStudio` sets `IsActive = false`. Returns 404 if studio not found. Returns 400
  if studio is already suspended.
- `UnsuspendStudio` sets `IsActive = true`. Returns 404 if studio not found. Returns 400
  if studio is already active.
- Both commands are `IssuerOnly`.
- Neither command deletes data.

---

### Layer B — Frontend State

#### B1. platformApi.ts

Read `frontend/src/features/platform/platformApi.ts` in full.

Verify:
- Every endpoint tag type is correct (invalidates the right caches).
- `extendTrial` invalidates `["PlatformSubscription", "PlatformStats"]`. ✓
- `activateSubscriptionManually` invalidates `["PlatformSubscription", "PlatformStats"]`. ✓
- `cancelSubscription` invalidates `["PlatformSubscription", "PlatformStats"]`. ✓
- `generateReferralCodeForStudio` invalidates `["PlatformReferral", "PlatformStats"]`. ✓
- `deactivateReferralCode`, `reactivateReferralCode`, `deleteReferralCode` all
  invalidate `["PlatformReferral"]`. ✓
- All mutation methods use the correct HTTP verb and URL. ✓
- No mutation uses RTK Query's `onQueryStarted` for optimistic updates (issuer data
  doesn't need them — confirm actions are deliberate, not UX-speed-sensitive).
- Check: is there a `getStudioById` endpoint for the issuer detail page? If missing
  and `IssuerStudioDetailPage` needs it, add it:
  ```ts
  getStudioById: builder.query<IssuerStudioDetailResponse, string>({
    query: (studioId) => `platform/studios/${studioId}`,
    providesTags: (_r, _e, id) => [{ type: "PlatformSubscription", id }],
  }),
  ```
  Add the `IssuerStudioDetailResponse` type to `platform.types.ts` if missing.

#### B2. platform.types.ts

Verify every type matches the backend response shape exactly:
- `PlatformStatsResponse` — all 11 fields present.
- `PlatformSubscriptionResponse` — includes `subscriptionId`, `status`, `planName`,
  `trialExpiresAt`, `currentPeriodEnd`. If `gracePeriodEnd` is needed anywhere in the
  UI, add it to both the type and the backend query projection.
- `PlatformReferralCodeResponse` — includes `isSingleUse` and `redemptionCount`.
- `IndustryReportSummary` — `period`, `generatedAt`, `downloadUrl`.
- `MrrDataPoint` — `month`, `mrr`.
- Add `IssuerStudioDetailResponse` if not present (see B1).

---

### Layer C — Frontend Components

For each component below: read the file in full, identify bugs, fix them, and ensure
the corresponding test file covers the fix.

#### C1. IssuerDashboardPage

Bugs to look for:
- `AtRiskRow.ExpiryLabel` uses `sub.trialExpiresAt` for both GracePeriod and PastDue
  labels. For GracePeriod studios, `currentPeriodEnd` is more relevant than
  `trialExpiresAt`. Verify logic and fix if wrong.
- The "Extend trial" input accepts values 1–90. Validate `parseInt` result before
  calling the mutation — show an inline error if out of range.
- The `→` link in `AtRiskRow` navigates to `/platform/studios` with `state: { highlight }`.
  Verify `IssuerStudioListPage` actually reads `location.state.highlight` and scrolls to
  or highlights that row. If not implemented, implement it.
- MRR displayed as `€0` when stats hasn't loaded yet — should show a skeleton, not `€0`.
  The `statsLoading` guard should cover this; confirm it does.
- ARPU shows `—` when `activeSubscriptions === 0`. Confirm this is rendered before
  `stats` is available (while loading). If ARPU shows `NaN` or crashes on null,
  fix the guard.

#### C2. IssuerStudioListPage

Bugs to look for:
- The `subMap` is populated from `getPlatformSubscriptions`, which returns ALL studios.
  When `studios` from `getStudiosQuery` loads separately, there's a window where
  `subMap` is empty and every studio shows "No subscription". This is cosmetically
  incorrect during loading. Verify the loading state (`isLoading`) correctly covers
  BOTH queries (currently `const isLoading = studiosLoading || subsLoading`).
  If one resolves before the other, the page shows partial data. Confirm this is
  correct or add a combined loading guard.
- `cashPlanId` initialized to `""`. If no plan is selected and "Activate" is clicked,
  the API is NOT called (guarded by `if (!cashPlanId) return`). But the button should
  be `disabled` visually when no plan is selected. Verify `disabled={activating_ || !cashPlanId}`
  is actually on the button.
- Status filter "Suspended" should match `!studio.isActive`, not a subscription status.
  Verify `effectiveStatus` correctly applies "Suspended" before subscription status.
- The `→` shortcut link on `AtRiskRow` (dashboard) navigates to this page with
  `location.state.highlight`. Implement reading that state and auto-scrolling to or
  visually highlighting the matching studio row. Use `useLocation` + `useRef`. Mark
  the highlighted row with a ring or border on first render, fade after 2 seconds.
- Search input: "Search by name or slug" — confirm that search is case-insensitive and
  trims whitespace. Current code does `s.name.toLowerCase().includes(q)` where
  `q = search.trim().toLowerCase()`. ✓ Verify it does the same for slug.
- Empty state (no studios at all) and "no match" state (filters returned nothing) are
  both present. Confirm they show distinct, helpful messages.

#### C3. IssuerStudioDetailPage

Read this file in full. This page is accessed from `IssuerStudioListPage` via the
"View" button at `/platform/studios/:id`.

Likely missing features (implement if absent):
- Studio name, slug, city, `isActive` status badge.
- Subscription status card (status, plan name, trial/period dates).
- Subscription action buttons (extend trial, activate manually, cancel) — reuse the
  same logic as `StudioRow` but in a dedicated page layout.
- Referral codes generated for this studio (list + generate button).
- Link back to `/platform/studios`.
- If the page is largely unimplemented (just a placeholder), implement it fully using
  the `getStudioById` query (add to `platformApi` if missing — see B1).

#### C4. PlanManagementPage

Bugs to look for:
- Create plan form: validate that `priceMonthly` is a positive number before submitting.
  If using a plain `<input type="number">`, ensure `min="0.01"` and step and that the
  value is parsed before sending.
- `AllowBrandingRemoval` toggle: verify this field is included in both the create and
  edit form payload. If it's missing from `UpdatePlanRequest` on the frontend, add it.
- Deleting a plan that has active subscriptions: the API returns 409. The frontend must
  show a descriptive error, not a generic "Something went wrong". Verify the mutation
  error handler reads `error.data.message` and surfaces it.
- Edit form pre-population: when the user clicks "Edit" on an existing plan, the form
  should pre-fill with current values. If using local state, verify that state is reset
  on each plan's edit open.
- Success feedback: after creating or editing a plan, confirm a visible success message
  (or toast) appears and the list refreshes. RTK Query invalidation should handle the
  refresh — verify `invalidatesTags` is set.

#### C5. SubscriptionOversightPage

Bugs to look for:
- Status filter: the URL query param (`?status=Active`) is referenced by KPI card links
  on the dashboard. Verify `SubscriptionOversightPage` reads `useSearchParams()` and
  applies the filter on load. If it doesn't, implement it.
- If `getplatformSubscriptions` returns an empty array (new platform, no studios yet),
  show an empty state with a helpful message, not a blank page.
- Date columns: trial expiry and period end should be formatted consistently — use
  `en-US` locale and include year. Verify the formatter matches `PlanManagementPage`.
- Sorting: the table (or card list) should be sortable by at least studio name and
  subscription status. If no sorting exists, add client-side sort by clicking column
  headers.
- Suspend/unsuspend is in `IssuerStudioListPage` but not in `SubscriptionOversightPage`.
  This is correct — verify there's a "View" link to the studio list from each row so
  the issuer can take action.

#### C6. PlatformReferralPage

Bugs to look for:
- `generateReferralCodeForStudio`: the mutation takes a `studioId`. The page likely has
  a studio selector. Verify the selector is populated (it must call
  `useGetStudiosQuery()`) and an empty selection is guarded before calling the mutation.
- `deleteReferralCode` should show a confirmation step before calling the mutation —
  deletion is permanent. If no confirmation exists, add an inline "Are you sure?" step
  (same pattern as suspend in `IssuerStudioListPage`).
- After deactivating a code, the "Deactivate" button should change to "Reactivate"
  without a page reload. RTK Query tag invalidation should handle this — verify the
  `PlatformReferral` tag is invalidated on both mutations.
- Codes with `expiresAt: null` must show "No expiry" (not a blank cell or "Invalid Date").
- If no referral codes exist, show an empty state.

#### C7. IndustryReportsPage / IndustryReportsPanel / MrrChart

Bugs to look for:
- `triggerIndustryReport` returns 202 Accepted (async). The frontend must show a "Report
  generation queued" message and NOT try to wait for the job to complete. Verify the
  success handler doesn't try to `invalidatesTags` the reports list immediately (the
  new report isn't there yet — Hangfire job takes minutes). If the UI refreshes
  immediately and shows 0 new reports, that's confusing. Show a banner: "Report queued.
  Check back in a few minutes."
- Reports list: `downloadUrl` is a signed R2 URL. The link should open in a new tab
  (`target="_blank" rel="noopener noreferrer"`). Verify.
- `MrrChart`: if `getMrrHistory` returns an empty array (new platform), the chart must
  not crash. Verify there's a fallback (`data ?? []`).
- `MrrChart`: if the recharts library isn't installed, the chart can't render. Verify
  the import resolves or replace with a simple SVG-based chart if recharts isn't in
  `package.json`. (Check `frontend/package.json` for `recharts`.)
- `IndustryReportsPanel` (if used as a dashboard sub-component): verify it doesn't
  duplicate the same RTK Query call already made in the dashboard — tag sharing should
  mean it's cached.

---

### Layer D — Tests

After fixing bugs in the code, ensure every issuer test file is complete.

For each test file, verify these minimum test cases exist. Add the missing ones.

#### D1. `IssuerDashboardPage.test.tsx`

Required tests:
- Renders KPI grid with loaded stats
- KPI grid shows skeletons while loading
- "Total Studios" KPI is a link to `/platform/studios`
- At-risk section: shows "No at-risk studios" when atRisk is empty
- At-risk section: shows studio name, status badge, days remaining
- At-risk: clicking "Extend trial" reveals the days input and Confirm button
- At-risk: Confirm calls `extendTrial` mutation with correct studioId and days
- At-risk: "→" link navigates to `/platform/studios`
- MRR chart renders without crashing on empty data

#### D2. `IssuerStudioListPage.test.tsx`

Required tests:
- Renders studio list with name, slug, status badge
- Loading state shows skeleton rows
- Search filters studios by name (case-insensitive)
- Search filters studios by slug
- Status filter "Suspended" shows only suspended studios
- Status filter "Active" shows only active studios
- "View" button links to `/platform/studios/:id`
- Suspend button → confirm step → calls `suspend` mutation
- Unsuspend button (when studio is suspended) → calls `unsuspend` mutation
- "Extend Trial" button appears for non-active studios
- Extend trial form: validates days > 0
- "Activate" button appears for activatable statuses
- Activate form: requires plan selection; button disabled when no plan
- Cancel subscription button with confirm step
- Empty state shows when no studios exist
- No-match state shows when filters produce no results

#### D3. `IssuerStudioDetailPage.test.tsx`

Required tests:
- Renders studio name, slug, city, status
- Renders subscription status card
- Back link to `/platform/studios`
- Shows referral codes for this studio
- Generate referral code button calls mutation

#### D4. `PlanManagementPage.test.tsx`

Required tests:
- Renders plan list (name, price, interval, branding removal toggle)
- Loading skeleton
- Create plan form renders with all fields
- Create validates empty name (shows error)
- Create validates price ≤ 0 (shows error)
- Create success refreshes list
- Edit pre-fills form with existing values
- Edit includes `AllowBrandingRemoval` checkbox
- Delete with active subscriptions shows 409 error message
- Delete without active subscriptions succeeds
- Empty state when no plans exist

#### D5. `SubscriptionOversightPage.test.tsx`

Required tests:
- Renders list with studio name, status badge, plan, dates
- Status filter from URL `?status=Active` pre-selects filter
- Status filter `?status=Trialing` shows only trialing subscriptions
- Empty state when no subscriptions
- Each row has a link to the studio detail

#### D6. `PlatformReferralPage.test.tsx`

Required tests:
- Renders referral code list
- Loading skeleton
- Studio selector populates from `getStudiosQuery`
- Generate button disabled when no studio selected
- Generate calls mutation with studioId
- Deactivate shows confirmation before calling mutation
- After deactivation: code shows "Inactive" and Reactivate button appears
- Reactivate calls mutation
- Delete shows confirmation, disabled for redeemed codes (redemptionCount > 0)
- `expiresAt: null` renders as "No expiry"
- Empty state when no referral codes exist

#### D7. `IndustryReportsPage.test.tsx`

Required tests:
- Renders list of reports with period and download link
- Download links open in new tab (`target="_blank"`)
- Trigger button calls mutation
- Trigger button shows "Queued" confirmation message (not list refresh)
- Empty state when no reports exist
- MrrChart renders without crashing on empty data

---

## Phase 1 Exit Condition

```
dotnet test   → All green
pnpm test     → All green
dotnet build  → 0 errors, 0 warnings
pnpm build    → 0 TypeScript errors
```

Do not exit Phase 1 until all four commands are clean.

---

# PHASE 2 — POLISH TO FINISHED PRODUCT

Phase 2 is a product completeness pass. Think like a product manager reviewing the
issuer section before a beta launch. Go through each criterion below. For each item
that is missing or incomplete, implement it. When done, re-run the test suite.

---

## P1. Navigation & Layout

### P1.1 Active nav highlighting
`IssuerLayout.tsx` uses `NavLink` with `isActive` → `bg-primary text-primary-foreground`.
Verify that the active state is clearly visible on both light AND dark themes.
`bg-primary` on dark may be too subtle. If so, change to
`bg-violet-600 text-white` for the active item — consistent with the rest of the app.

### P1.2 Mobile nav
The current nav is a horizontal row inside the header. On screens narrower than 640px,
all 6 nav items will overflow. Add a horizontal-scroll container with
`overflow-x-auto scrollbar-none` around the nav, OR collapse to a mobile hamburger menu.
The simpler horizontal-scroll approach:
```tsx
<nav className="ml-6 flex items-center gap-1 overflow-x-auto scrollbar-none">
```

### P1.3 Page title in browser tab
Every issuer page must call `useDocumentMeta()` (already in the project) with a
descriptive title. Check every issuer component:
- `IssuerDashboardPage`: "Platform Overview — Pena e Artë"
- `IssuerStudioListPage`: "Studios — Platform Admin — Pena e Artë"
- `IssuerStudioDetailPage`: "{Studio Name} — Platform Admin — Pena e Artë"
- `PlanManagementPage`: "Plans — Platform Admin — Pena e Artë"
- `SubscriptionOversightPage`: "Subscriptions — Platform Admin — Pena e Artë"
- `PlatformReferralPage`: "Referral Codes — Platform Admin — Pena e Artë"
- `IndustryReportsPage`: "Industry Reports — Platform Admin — Pena e Artë"

Add `useDocumentMeta(...)` to any page that's missing it. Import from
`@/shared/utils/useDocumentMeta`.

### P1.4 Breadcrumbs on deep pages
`IssuerStudioDetailPage` is a child of the studios list. Add a breadcrumb row:
```
Platform Admin → Studios → {studioName}
```
Use `Link` for the first two segments, plain text for the last.

---

## P2. KPI Dashboard Polish

### P2.1 MRR chart enhancement
The `MrrChart` component shows MRR over time. Polish:
- Add a period selector: "3M | 6M | 12M" (three buttons, default 12M). Pass `months`
  to `getMrrHistory`. When the user changes the period, the chart refetches.
- Format Y-axis values as `€N` (thousands as `€1.2k`).
- Show a "No data yet" empty state when the array is empty, instead of an empty chart.
- Add `aria-label="MRR over time"` on the chart's root element.

### P2.2 Dashboard "new this month" caveat
The caveat logic `"incl. test data"` fires if >50% of studios are new this month.
This threshold is too aggressive for a real platform. Change to: if `newStudiosThisMonth === totalStudios`,
show "(all studios registered this month — may include test accounts)" instead.

### P2.3 KPI card subtitles
Some KPI cards have subtitles like "current" that are vague. Polish:
- "Total Studios" → subtitle: "registered on platform"
- "Active Subscriptions" → subtitle: "paying customers"
- "MRR" → subtitle: keep the growth percent
- "ARPU" → subtitle: "avg revenue per active studio"
- "Trial Conversion" → subtitle: "trial → paid rate"

### P2.4 At-risk section — trial expiry context
The `ExpiryLabel` component currently shows "X days left" for GracePeriod studios
using `trialExpiresAt`. For GracePeriod, the relevant date is `currentPeriodEnd`
(when the grace period ends), not the trial. Update `AtRiskRow` to pass the correct
date to `ExpiryLabel` based on status:
- Trialing → `trialExpiresAt`
- GracePeriod → `currentPeriodEnd`
- PastDue → show "Payment overdue" (already correct)

---

## P3. Studio List Polish

### P3.1 Highlight arriving from dashboard
Implement the `location.state.highlight` feature described in C2.
When `IssuerStudioListPage` mounts with `state.highlight = studioId`, scroll that
studio's card into view and briefly highlight it with `ring-2 ring-violet-500`.
Remove the ring after 2.5 seconds.

Implementation:
```tsx
const location  = useLocation();
const highlight = (location.state as { highlight?: string } | null)?.highlight ?? null;
const rowRefs   = useRef<Map<string, HTMLDivElement>>(new Map());

// Approved useEffect: DOM scroll-to side-effect, not data fetching.
useEffect(() => {
  if (!highlight) return;
  const el = rowRefs.current.get(highlight);
  if (el) {
    el.scrollIntoView({ behavior: "smooth", block: "center" });
  }
}, [highlight, filtered]);

// In StudioRow's outer <Card>:
<Card
  ref={(el) => { if (el) rowRefs.current.set(studio.id, el as HTMLDivElement); }}
  className={cn(
    isSuspended ? "border-destructive/40" : "",
    highlight === studio.id ? "ring-2 ring-violet-500 ring-offset-2" : "",
  )}
>
```

Remove the ring after 2.5 seconds by passing `highlight` and tracking a `cleared` state.

### P3.2 Studio count in header
The studio list header shows `N studios` or `N of M`. Also show:
- Number of active subscriptions: "12 active · 3 trialing · 1 at risk"
- Computed from `subMap` and the `studios` array.

### P3.3 Sort order
Default sort: suspended first, then at-risk (GracePeriod/PastDue), then Trialing,
then Active, then Cancelled/NoSubscription. Within each group: alphabetical by name.
This ensures the issuer sees the most urgent studios first without needing to filter.

Add a sort function applied after the filter in `filtered` useMemo:

```ts
const SORT_PRIORITY: Record<string, number> = {
  Suspended:      0,
  PastDue:        1,
  GracePeriod:    2,
  Trialing:       3,
  Active:         4,
  Cancelled:      5,
  NoSubscription: 6,
};
```

---

## P4. Plan Management Polish

### P4.1 Plan card layout
If plans are shown as a table, consider a card-per-plan layout instead — it's more
scannable and allows showing all actions inline without column constraints.

Each plan card should show:
- Plan name (large, bold)
- `Monthly: €X · Yearly: €Y (save 17%)`
- `AllowBrandingRemoval: Yes / No` (badge)
- Edit button (opens inline edit form) + Delete button (with confirm)
- Number of studios on this plan (from subscription data)

### P4.2 Yearly price auto-calculation
When the issuer sets `priceMonthly`, the yearly price should auto-suggest
`priceMonthly * 10` (2 months free). Show this as a helper text below the yearly
price field: "Suggested: €{priceMonthly * 10} (2 months free)".
The issuer can override it.

### P4.3 "Studios on this plan" count
Join subscription data to show how many studios are on each plan. This helps the
issuer decide whether it's safe to delete or edit a plan.
Source: from `getPlatformSubscriptions`, count by `planName` matching.

---

## P5. Subscription Oversight Polish

### P5.1 URL-driven filtering
The dashboard KPI cards link to `/platform/subscriptions?status=Active` etc.
Implement reading `status` from `useSearchParams()` and applying it to the filter
on initial render. When the filter changes in the UI, also update the URL param
(via `setSearchParams`) so the URL is bookmarkable.

### P5.2 Table sort
Allow sorting by:
- Studio name (A→Z, Z→A)
- Subscription status
- Trial/period end date (soonest first — most urgent)

Default: trial/period end date ascending (most urgent first).
Use client-side sort on the `subscriptions` array.

### P5.3 Row actions
Each row in `SubscriptionOversightPage` should have a "View" link to
`/platform/studios/:id` for the studio detail page.

---

## P6. Referral Codes Polish

### P6.1 Code copy button
Each referral code in the list should have a small "Copy" button next to the code string.
Use `navigator.clipboard.writeText(code)` in an onClick handler.
Show a brief "Copied!" tooltip or inline text after copying.
Do NOT use `useEffect` for this — the clipboard API call is in the event handler.

### P6.2 Filter by studio
Add a "Filter by studio" dropdown above the referral codes list, populated from
`useGetStudiosQuery()`. Filtering is client-side (`referralCodes.filter(c => c.studioId === selectedId)`).
When the generate form has a studio selected, pre-select that studio in the filter.

### P6.3 Expiry date picker
The generate form should include an optional expiry date field
(`<input type="date" />`). If left empty, the code never expires.
Pass `expiresAt?: string | null` in the generate mutation body.

---

## P7. Industry Reports Polish

### P7.1 Last generated timestamp
Each report card should show "Generated: {date}" alongside the period and download link.
Already in `IndustryReportSummary.generatedAt` — ensure it's rendered.

### P7.2 Trigger button cooldown
After clicking "Trigger report generation", disable the button for 60 seconds to
prevent accidental double-queueing. Show a countdown: "Next trigger available in 58s".
Use a `useEffect` with a 1-second interval for the countdown timer.
This is an approved `useEffect` (timer side-effect, not data fetching).

### P7.3 Report summary preview
If the report JSON contains a short summary object, show key metrics inline
(without downloading the full JSON) — e.g., "Avg bookings: 23 · Trial conversion: 34%".
This requires an additional backend endpoint:
`GET /api/v1/platform/reports/industry/{period}/summary` returning a compact object.
Implement only if the backend already writes a summary field to R2. Otherwise, leave
as a download-only flow.

---

## P8. Global Polish Items

### P8.1 Toast notifications for all mutations
Every issuer mutation should fire a Sonner toast on success and on error:

```
Success:
  - Extend trial:      "Trial extended for {studioName}"
  - Suspend:           "{studioName} suspended"
  - Unsuspend:         "{studioName} reactivated"
  - Activate (cash):   "Subscription activated for {studioName}"
  - Cancel sub:        "Subscription cancelled for {studioName}"
  - Create plan:       "Plan '{name}' created"
  - Update plan:       "Plan '{name}' updated"
  - Delete plan:       "Plan '{name}' deleted"
  - Deactivate code:   "Referral code {code} deactivated"
  - Reactivate code:   "Referral code {code} reactivated"
  - Delete code:       "Referral code {code} deleted"
  - Generate code:     "Referral code generated: {code}"
  - Trigger report:    "Report generation queued"

Error (generic):
  Show the error `data.message` if present, else "Action failed. Try again."
```

Use `import { toast } from "sonner"` — already in the project. Call `toast.success()`
and `toast.error()` in the mutation `onSuccess`/`onError` handlers or in the component's
`try/catch`.

### P8.2 Confirm dialogs for all destructive actions

Every destructive action must have an inline confirmation step before executing:
- Suspend studio
- Cancel subscription
- Delete plan
- Delete referral code

The pattern from `IssuerStudioListPage` is the standard: show "Action?" text + Yes/No
buttons inline. Use this same pattern everywhere it's missing.

### P8.3 Loading spinners on all mutation buttons

Every button that triggers a mutation must:
1. Show `<Loader2 className="h-3 w-3 animate-spin" />` while the mutation is in flight.
2. Be `disabled` during the mutation.

Audit every action button in every issuer component and add the loading pattern where missing.

### P8.4 Accessibility — every action button needs aria-label

Every icon-only button (like the `→` link in `AtRiskRow`) needs a visible label or
`aria-label`. Audit all icon-only interactive elements in issuer components.

### P8.5 Error boundaries

Issuer pages load multiple async queries. If one fails, the page shouldn't crash.
Verify each component handles `isError` from every RTK Query call. Every query should
have an error state that shows:
```
Failed to load {data type}. Please refresh or try again.
```
with a "Retry" button that calls `refetch()` from the query result.

---

## Phase 2 Exit Condition

After completing all polish items:

1. Run `pnpm test` — all green.
2. Run `dotnet test` — all green.
3. Run `pnpm build` — no TypeScript errors.
4. Run `dotnet build` — no warnings.
5. Self-review: navigate mentally through every issuer page as if you are the platform
   admin. For each page, answer:
   - Does it have a page title in the browser tab?
   - Does every async section have a loading state?
   - Does every async section have an error state with a retry?
   - Does every empty state have a helpful message?
   - Does every destructive action have a confirmation?
   - Does every mutation show a toast on success and error?
   - Does every form show validation errors below the relevant field?
   - Do all buttons show a spinner while in-flight?
   If any answer is No, fix it before declaring done.

---

## Final Deliverable

When both phases exit cleanly, add an entry to `docs/claude/architecture.md`
under a new heading `## Issuer QA Pass — 2026-07-01` listing:

1. Every bug found and fixed (one line each: file → bug → fix).
2. Every polish item implemented.
3. Any architectural decisions made (if a new pattern was introduced).
4. Any items that were out of scope or skipped, with a reason.

Keep it concise — this is a log for future reference, not a narrative.
