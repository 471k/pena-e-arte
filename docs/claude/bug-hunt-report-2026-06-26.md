# Bug Hunt Report — 2026-06-26

Automated overnight scan per `docs/claude/self-promotion-prompts.md` protocol.
All fixes applied and verified in a single session.

---

## Baseline

| Metric | Before | After |
|---|---|---|
| Backend build errors | 0 | 0 |
| Backend test failures | 0/1117 | 0/1117 |
| Frontend test failures | 15/1155 | 0/1155 |
| ESLint errors | 9 | 0 |
| TypeScript errors | 0 | 0 |

---

## Fixes Applied

### P0 — Critical

#### FIX-01 · `publicApi.ts` — TypeError crash in test stores without auth reducer

**File:** `frontend/src/features/public/publicApi.ts:105`

**Bug:** `prepareHeaders` read `(getState() as RootState).auth.token` without optional chaining. When a test store is configured with only `publicApi.reducer` (no `auth` slice), `getState().auth` is `undefined` → TypeError at runtime.

**Impact:** 14 frontend tests failing (9 in `PortfolioFeed.test.tsx`, 5 in `DiscoverPage.test.tsx`).

**Fix:**
```typescript
// Before
const token = (getState() as RootState).auth.token;
// After
const token = (getState() as RootState).auth?.token;
```

---

#### FIX-02 · `RegisterStudioPage.tsx` — Password mismatch validation never surfaces

**File:** `frontend/src/features/studios/components/RegisterStudioPage.tsx:53-56`

**Bug:** Zod v4.4.3 + `@hookform/resolvers` v5.4.0: `.refine()` with a `path` option does not reliably propagate cross-field errors to `errors.confirmPassword` via `zodResolver`. The error is swallowed silently, so the "Passwords do not match" message is never shown to the user.

**Impact:** 1 frontend test timing out (5119 ms vs 5000 ms default).

**Fix:** Replaced `.refine()` with `.superRefine()` using `ctx.addIssue()`:
```typescript
.superRefine((data, ctx) => {
  if (data.password !== data.confirmPassword) {
    ctx.addIssue({
      code: "custom",
      message: "Passwords do not match",
      path: ["confirmPassword"],
    });
  }
});
```

---

### P1 — High

#### FIX-03 · `StripeBillingService.cs` — `.First()` throws when Stripe items/phases are empty (B-01)

**File:** `Pena_e_Arte.Infrastructure/Services/StripeBillingService.cs`

**Bug (line 102):** `sub.Items.Data.First().Id` — throws `InvalidOperationException` if Stripe returns a subscription with zero items (e.g. transition states, misconfigured plans).

**Bug (line 126):** `schedule.Phases.First()` — throws if the newly-created `SubscriptionSchedule` has no phases (Stripe API contract not guaranteed synchronously).

**Fix:** Replaced both with `.FirstOrDefault()` + null-coalescing throw:
```csharp
// Line 102
string itemId = sub.Items?.Data?.FirstOrDefault()?.Id
    ?? throw new InvalidOperationException($"Stripe subscription {stripeSubscriptionId} has no items.");

// Line 126
SubscriptionSchedulePhase currentPhase = schedule.Phases?.FirstOrDefault()
    ?? throw new InvalidOperationException($"Stripe schedule for subscription {stripeSubscriptionId} has no phases.");
```

---

#### FIX-04 · `PublicEndpoints.cs` — `GetPortfolioFeed` requires `radiusKm` and `page` (B-09)

**File:** `Pena_e_Arte.API/Endpoints/PublicEndpoints.cs`

**Bug:** `radiusKm` and `page` were required query parameters with no defaults. Clients that omit them receive a `400 Bad Request` even though sensible defaults exist.

**Fix:** Made both optional with defaults and clamped `pageSize`:
```csharp
double radiusKm = 50,
int    page     = 1,
int    pageSize = 24)
{
    if (pageSize is < 1 or > 100) pageSize = 24;
```

---

#### FIX-05 · `PublicEndpoints.cs` — `GetNearbyStudios` accepts out-of-range coordinates (B-10)

**File:** `Pena_e_Arte.API/Endpoints/PublicEndpoints.cs`

**Bug:** No validation on `lat` (must be -90..90), `lng` (-180..180), or `radiusKm` (must be > 0 and ≤ 500). Invalid values pass through to the DB query and return nonsensical results or trigger MySQL spatial errors.

**Fix:** Added guard before the MediatR send:
```csharp
if (lat is < -90 or > 90 || lng is < -180 or > 180 || radiusKm is <= 0 or > 500)
    return Results.BadRequest("Invalid lat/lng/radiusKm.");
```

---

### P2 — Medium (ESLint errors)

#### FIX-06 · `BillingPage.tsx` — `useMemo` dep array too narrow (`react-hooks/preserve-manual-memoization`)

**File:** `frontend/src/features/billing/components/BillingPage.tsx:101`

**Fix:** Changed `[sub?.planId, plans]` → `[sub, plans]` (React Compiler infers `sub` as the dependency, not its property).

---

#### FIX-07 · `DashboardPage.tsx` — non-component export in component file (`react-refresh/only-export-components`)

**File:** `frontend/src/features/dashboard/components/DashboardPage.tsx:62`

**Note:** `bannerConfig` is exported and consumed by `bannerConfig.test.tsx` — it must remain exported. Added `// eslint-disable-next-line react-refresh/only-export-components` comment.

---

#### FIX-08 · `ArtistListPage.tsx` — `setSelectedSpec` in effect (`react-hooks/set-state-in-effect`)

**File:** `frontend/src/features/artists/components/ArtistListPage.tsx:77`

**Note:** Intentional derived-state reset (clear filter chip when search query changes). Added disable comment.

---

#### FIX-09 · `NotificationPreferencesCard.tsx` — `setLocal` in effect (`react-hooks/set-state-in-effect`)

**File:** `frontend/src/features/notifications/components/NotificationPreferencesCard.tsx:54`

**Note:** Standard server-data → editable-local-state sync pattern. Added disable comment.

---

#### FIX-10 · `location-picker.tsx` — `setPin` in effect (`react-hooks/set-state-in-effect`)

**File:** `frontend/src/shared/components/ui/location-picker.tsx:110`

**Note:** External `value` prop arriving asynchronously (form reset from API) must sync into local state. Added disable comment.

---

#### FIX-11 · `authSlice.test.ts` — unused destructured variables (`@typescript-eslint/no-unused-vars`)

**File:** `frontend/src/features/auth/__tests__/authSlice.test.ts:93,120`

**Fix:** Added `// eslint-disable-next-line @typescript-eslint/no-unused-vars` before each destructuring; `exp` and `_` are intentional omit-patterns.

---

#### FIX-12 · `PaymentListPage.test.tsx` — unused `callCount` variable

**File:** `frontend/src/features/payments/__tests__/PaymentListPage.test.tsx:241`

**Fix:** Removed `let callCount = 0;` and the `callCount++` increment inside the handler (variable was never asserted).

---

#### FIX-13 · `useSignalR.test.tsx` — misplaced `@typescript-eslint/no-explicit-any` disable directive

**File:** `frontend/src/shared/hooks/__tests__/useSignalR.test.tsx:65,70`

**Fix:** Removed unused directive on line 65; moved it to just before `) as any,` on line 70.

---

#### FIX-14 · `PortfolioFeed.test.tsx` — stale expected artist URL

**File:** `frontend/src/features/public/__tests__/PortfolioFeed.test.tsx:122`

**Bug:** Test expected `/a/ana-lima` but the router defines `/artist/:slug` and the component links to `/artist/${image.artistSlug}`.

**Fix:** Updated assertion to `/artist/ana-lima`.

---

## Scans With No Issues Found

| Category | Result |
|---|---|
| B-02 Unprotected endpoints | Clean — all public routes use `AllowAnonymous` explicitly; all others have `.RequireAuthorization()` |
| B-03 PII in logs | Clean — no names, emails, phone numbers or card data in any log statement |
| B-04 IgnoreQueryFilters audit | Clean — all usages have either `// Approved:` comments or explicit `DeletedAt == null` predicates |
| B-05 Unbounded ToListAsync | Clean — all list queries are tenant-scoped or explicitly paginated |
| B-06 External calls without try/catch | Clean — `AppointmentReminderJob`, `TrialExpiryWarningJob`, `SendAppointmentConfirmationCommand` all wrap email/SMS in `try/catch` |
| B-07 Missing FluentValidation | Clean — every command/query with a matching validator registered |
| B-08 Null-forgiving operators | No `!.` on values that could be null in production paths |
| B-11 SendAppointmentConfirmationCommand | Clean — email and SMS paths both have proper error handling |
| B-12 DeletedAt consistency | Clean — all IgnoreQueryFilters usages in jobs explicitly filter `DeletedAt == null` |
| F-01 useEffect for data fetching | Clean — RTK Query used throughout |
| F-02 TypeScript `any` | Cleaned up (FIX-13) |
| F-03 Default exports | Clean — all components use named exports |
| F-04 console.log in production paths | Clean |
| F-05 localStorage outside authSlice | Clean |
| F-06 RTK cache invalidation gaps | Clean |
| F-07 SignalR cleanup | Clean — `useSignalR` calls `stop()` in cleanup |
| F-08 Hardcoded URLs | Clean |

---

## Final State

```
Backend:   848 unit + 269 integration = 1117 tests, 0 failures
Frontend:  1155 tests, 0 failures
ESLint:    0 errors, 10 warnings (all pre-existing React Compiler informational warnings)
TypeScript: 0 errors
```

The single "failed test file" in the Vitest run is `e2e/critical-path.spec.ts` — a Playwright spec accidentally included in Vitest's discovery glob. It was pre-existing before this session and is unrelated to any change made here. All 1155 unit/component tests pass.
