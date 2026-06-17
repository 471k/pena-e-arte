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

_All P1 issues resolved. See closed items below._

~~### #4 — ClientLayout.tsx missing~~
~~**Fixed:** `layouts/ClientLayout.tsx` exists with full nav + `ReadOnlyBanner`.~~

~~### #5 — ArtistLayout.tsx missing~~
~~**Fixed:** `layouts/ArtistLayout.tsx` exists with full nav + `ReadOnlyBanner` + `useSignalR`.~~

~~### #6 — OwnerLayout.tsx missing~~
~~**Fixed:** `layouts/OwnerLayout.tsx` exists with full nav + `SuspensionBanner` + `ReadOnlyBanner` + `useSignalR`.~~

~~### #7 — ReadOnlyBanner placed inline per-page~~
~~**Fixed:** Layouts own the banner. Removed duplicate inline usage from `SignConsentFormPage` and `SubmitIntakeFormPage` (2026-06-17).~~

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

_All P3 issues resolved._

~~### #18 — useCurrentUser hook missing~~
~~**Fixed:** `shared/hooks/useCurrentUser.ts` exists.~~

~~### #19 — notificationsSlice.ts missing~~
~~**Fixed:** `features/notifications/notificationsSlice.ts` exists with `unreadCount` + `isInboxOpen`.~~

~~### #20 — sessionExpired never triggers logout~~
~~**Fixed:** `baseQuery.ts` dispatches `setSessionExpired()` on 401; `AppRoot` in `router.tsx` handles redirect.~~

---

## P4 — Route and Access Control Gaps

_All P4 issues resolved._

~~### #21 — Client has no navigation after /book~~
~~**Fixed:** `AppLayout` dispatches to `ClientLayout` for all client routes.~~

~~### #22 — /dashboard reachable by artists and clients~~
~~**Fixed:** Route has `RoleGuard allowedRoles={[Role.Owner, Role.Issuer]}`.~~

~~### #23 — /schedule reachable by owners and issuers~~
~~**Fixed (intentionally different):** Owners allowed — `RoleGuard allowedRoles={[Role.Artist, Role.Owner, Role.Issuer]}`. Owner QuickNav links to schedule; this is intentional.~~

---

## P5 — Code Quality / Doc Staleness

~~### #24 — frontend.md shows TypeScript enum (wrong)~~
~~**Fixed:** `roles.ts` uses `const Role = { ... }` pattern; `frontend.md` already updated.~~

### #25 — Raw `<select>` with 5-line inline Tailwind class
- **Where:** `BookAppointmentForm.tsx`
- **What:** `selectClass` constant duplicates shadcn Input styling manually. Resolved when P2 #8 is done.

### #26 — Raw `<textarea>` with 4-line inline Tailwind class
- **Where:** `SubmitIntakeFormPage.tsx` — `TEXTAREA_CLS` and `SELECT_CLS` constants also present
- **What:** No `Textarea` shadcn component. Resolved when P2 #9 is done.

~~### #27 — Each page builds its own sticky header~~
~~**Fixed:** Layouts own the nav header; pages retain page-level back-nav headers (expected).~~

~~### #28 — IssuerLayout.tsx in wrong directory~~
~~**Fixed:** `layouts/IssuerLayout.tsx` in correct location.~~

~~### #29 — store.ts diverges from frontend.md spec~~
~~**Fixed:** `frontend.md` updated to reflect actual reducers.~~

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
