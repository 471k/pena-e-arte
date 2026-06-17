# Known Issues — Prioritized Backlog

> Last updated: 2026-06-17
> Total: 33 issues across 7 priority levels.
> Start every session by checking this file and fixing P0 first.

---

## P0 — Active Bugs (broken right now)

_All P0 bugs resolved. See closed items below._

~~### #1 — DesignRevisionUploaded invalidates wrong cache~~
~~**Fixed:** `useSignalR.ts` already dispatches `designsApi.util.invalidateTags(["Design"])` (resolved before 2026-06-17).~~

~~### #2 — Subscription status string casing mismatch~~
~~**Fixed:** Backend uses `.Status.ToString()` (PascalCase); frontend matches PascalCase throughout. `bannerConfig.test.tsx` pins the contract.~~

~~### #3 — JWT double-decoded in authSlice~~
~~**Fixed:** `authSlice.ts` calls `decodeToken` once and reads `payload.exp` directly.~~

---

## P1 — Missing Core UI Structure

### #4 — ClientLayout.tsx missing
- **Where:** `frontend/src/layouts/` (file does not exist)
- **What:** Clients land on `/book` with no navigation to tattoo history, designs, forms, or any other page.
- **Fix:** Create `layouts/ClientLayout.tsx` with a nav linking to: Book (`/book`), My Designs (`/designs`), My Forms (`/forms/intake`, `/forms/consent`), My Profile (`/clients/me`).

### #5 — ArtistLayout.tsx missing
- **Where:** `frontend/src/layouts/` (file does not exist)
- **What:** Artists on `/schedule` have no navigation to clients, designs, forms, deposit-rules, or notifications.
- **Fix:** Create `layouts/ArtistLayout.tsx` with a nav linking to: Schedule (`/schedule`), Clients (`/clients`), Designs (`/designs`), Forms (`/forms`), Deposit Rules (`/deposit-rules`), Notifications (`/notifications`).

### #6 — OwnerLayout.tsx missing
- **Where:** `frontend/src/layouts/` (file does not exist)
- **What:** Owner `/dashboard` has a QuickNav tile grid but no persistent sidebar/header nav — inconsistent with `IssuerLayout` which has a proper nav bar.
- **Fix:** Create `layouts/OwnerLayout.tsx` matching the pattern of `IssuerLayout`. Nav links: Dashboard (`/dashboard`), Artists (`/artists`), Clients (`/clients`), Designs (`/designs`), Payments (`/payments`), Billing (`/billing`), Studio Settings (`/studios/me`).

### #7 — ReadOnlyBanner placed inline per-page
- **Where:** `DashboardPage`, `SchedulePage`
- **What:** Should be rendered once in each role layout, not manually repeated in every page. Missing layouts are the root cause.
- **Fix:** Move `<ReadOnlyBanner />` into each layout component. Remove inline placement from individual pages.

---

## P2 — Missing Shared UI Components

All absent from `frontend/src/shared/components/ui/`. Use shadcn/ui primitives — do not write from scratch.

| # | Component | Used by |
|---|---|---|
| 8 | `select.tsx` | `BookAppointmentForm`, `CreateArtistPage`, `CreateClientPage`, `CreateDepositRulePage` — all use raw `<select>` |
| 9 | `textarea.tsx` | `BookAppointmentForm` notes field — raw `<textarea>` with 4-line inline class block |
| 10 | `badge.tsx` | `AppointmentStatusBadge`, `DepositStatusBadge` — both render custom inline spans |
| 11 | `skeleton.tsx` | Every list/detail page shows `Loader2` spinner — no skeleton loaders |
| 12 | `dialog.tsx` | Cancel appointment, delete design, delete tattoo record — actions are fire-and-forget with no confirmation |
| 13 | `toast.tsx` / Sonner | Success/error feedback after mutations — no toast system; success states are inline-form only |
| 14 | `table.tsx` / DataTable | Client, artist, payment, form list pages — lists render as stacked cards, no table component |
| 15 | `avatar.tsx` | Artist/client cards — mentioned in frontend.md spec |
| 16 | `separator.tsx` | Sectional dividers |
| 17 | `tabs.tsx` | `ClientDetailPage`, `ArtistDetailPage` (profile tabs) |

After adding each component: replace all raw HTML usages in consuming files. Run `pnpm lint` after each replacement.

---

## P3 — Missing Hooks and Slices

### #18 — useCurrentUser hook missing
- **Where:** `frontend/src/shared/hooks/` (does not exist)
- **What:** Documented in `frontend.md` as required. Currently replaced by scattered inline `useAppSelector((s) => s.auth.user)` calls in components.
- **Fix:** Create `shared/hooks/useCurrentUser.ts`. Replace all inline selector calls.

### #19 — notificationsSlice.ts missing
- **Where:** `frontend/src/features/notifications/` (does not exist)
- **What:** `frontend.md` store spec includes `notifications: notificationReducer` for local UI notification state (unread count, inbox open/close). Store currently has only `notificationsApi` — no local state slice.
- **Fix:** Create `features/notifications/notificationsSlice.ts` with `unreadCount: number`, `isInboxOpen: boolean`. Add `notificationsReducer` to store.

### #20 — sessionExpired never triggers logout
- **Where:** `frontend/src/features/ui/uiSlice.ts:18`
- **What:** `sessionExpired: boolean` flag exists and `setSessionExpired` is exported, but nothing in `baseQuery.ts` or `authSlice.ts` dispatches it or uses it to redirect to `/login` on 401 responses. Token expiry is silently ignored.
- **Fix:** In `baseQuery.ts`, on 401 response, dispatch `setSessionExpired(true)`. In `app/router.tsx` or a top-level component, listen for `sessionExpired === true` and redirect to `/login` then clear the flag.

---

## P4 — Route and Access Control Gaps

### #21 — Client has no navigation after /book
- **Where:** `app/router.tsx`
- **What:** No layout wraps client routes. Routes for `/designs/:id` (client view), `/forms/intake/new`, `/forms/consent/new` exist but are unreachable without layout nav.
- **Fix:** Wrap client routes with `<ClientLayout />` (created in P1 #4).

### #22 — /dashboard reachable by artists and clients
- **Where:** `app/router.tsx:63`
- **What:** `DashboardPage` is inside the global `RoleGuard` that allows all roles. Artists and clients can navigate to `/dashboard`.
- **Fix:** Add a nested `<RoleGuard allowedRoles={["owner", "issuer"]} />` around the `dashboard` route.

### #23 — /schedule reachable by owners and issuers
- **Where:** `app/router.tsx`
- **What:** No guard on `"schedule"` path — owners and issuers can hit it.
- **Fix:** Add a nested `<RoleGuard allowedRoles={["artist", "issuer"]} />` around the `schedule` route.

---

## P5 — Code Quality / Doc Staleness

### #24 — frontend.md shows TypeScript enum (wrong)
- **Where:** `docs/claude/frontend.md:218`
- **What:** Example shows `export enum Role { ... }` but the project uses `erasableSyntaxOnly: true`, which disallows TypeScript enums. Doc is stale and misleading.
- **Fix:** Update `frontend.md` to show the correct const-object + type-alias pattern.

### #25 — Raw `<select>` with 5-line inline Tailwind class
- **Where:** `BookAppointmentForm.tsx:28–33`
- **What:** `selectClass` constant duplicates shadcn Input styling manually. Resolved by P2 #8.

### #26 — Raw `<textarea>` with 4-line inline Tailwind class
- **Where:** `BookAppointmentForm.tsx:169–177`
- **What:** No `Textarea` component. Resolved by P2 #9.

### #27 — Each page builds its own sticky header
- **Where:** `SchedulePage`, `DashboardPage`, `BookPage`, `ClientListPage`, etc.
- **What:** Every page re-implements `<header className="flex items-center ... sticky top-0 z-10">`. Resolved by P1 layouts.

### #28 — IssuerLayout.tsx in wrong directory
- **Where:** `frontend/src/features/platform/components/IssuerLayout.tsx`
- **What:** Should be in `layouts/` per `frontend.md` spec. Sets wrong precedent.
- **Fix:** Move to `frontend/src/layouts/IssuerLayout.tsx`. Update all imports.

### #29 — store.ts diverges from frontend.md spec
- **Where:** `frontend/src/app/store.ts`
- **What:** Spec shows 5 API slices; actual store has more (`authApi`, `filesApi`, `intakeFormsApi`, `consentFormsApi`, `depositRulesApi`). Not a bug, but the spec in `frontend.md` is outdated.
- **Fix:** Update `frontend.md` store spec to reflect the actual reducers.

---

## P6 — Backend Missing Jobs / Hub Design

### #30 — Only one SignalR hub (ScheduleHub)
- **What:** Design approval and notification events go through `ScheduleHub`, conflating scheduling concerns with design/notification concerns.
- **Fix:** Create `Pena_e_Arte.Infrastructure/Hubs/DesignHub.cs` for design events. Create `NotificationHub.cs` for `NotificationReceived`. Update `IHubContext` injections accordingly.

### #31 — No background job for design revision timeout
- **What:** No Hangfire job to auto-expire a design revision awaiting client approval after N days.
- **Fix:** Create `Infrastructure/Jobs/DesignRevisionTimeoutJob.cs`. Schedule on `UploadDesignRevisionCommand` handler. N = configurable via app settings (default 14 days).

### #32 — No background job for payment reconciliation
- **What:** No Hangfire job to verify Stripe Connect payouts and mark sessions as settled.
- **Fix:** Create `Infrastructure/Jobs/PaymentReconciliationJob.cs`. Runs nightly via Hangfire recurring job.

---

## P7 — Test Coverage

| # | What | Detail |
|---|---|---|
| 33 | Frontend test coverage ~3% | Only `artists.test.tsx` and `clients.test.tsx` exist. No tests for: auth, appointments, billing, designs, forms, payments, notifications, deposit-rules, store, hooks |
| 34 | No auth flow tests | Login, role routing, token expiry, logout — none tested |
| 35 | No RTK Query endpoint tests | None of the API slices have tests verifying request shape or response handling |
| 36 | No E2E test setup | No Playwright or Cypress configured |
