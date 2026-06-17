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

_All P2 issues resolved (2026-06-17). Components existed; raw-HTML consumers updated._

~~All absent from `frontend/src/shared/components/ui/`.~~

| # | Component | Status |
|---|---|---|
| 8 | `select.tsx` | ✓ Exists. `SubmitIntakeFormPage` migrated to `<Select>` + `Controller` (2026-06-17). Others already using it. |
| 9 | `textarea.tsx` | ✓ Exists. `SubmitIntakeFormPage` migrated to `<Textarea>` (2026-06-17). |
| 10 | `badge.tsx` | ✓ Exists. `AppointmentStatusBadge`, `DepositStatusBadge`, `PaymentStatusBadge` all use `<Badge>`. |
| 11 | `skeleton.tsx` | ✓ Exists. All list/detail pages use `<Skeleton>`. |
| 12 | `dialog.tsx` | ✓ Exists. `AppointmentDetailPage`, `DesignDetailPage` use `<Dialog>` for confirmation. |
| 13 | `sonner.tsx` | ✓ Exists. All mutation pages use `toast.success` / `toast.error`. |
| 14 | `table.tsx` | ✓ Exists. `ClientListPage`, `ArtistListPage`, `PaymentListPage` use `<DataTable>`. |
| 15 | `avatar.tsx` | ✓ Exists. `ArtistDetailPage` and `ClientDetailPage` migrated to `<Avatar>` + `<AvatarFallback>` (2026-06-17). |
| 16 | `separator.tsx` | ✓ Exists. Used in `DashboardPage`, `SchedulePage`, etc. |
| 17 | `tabs.tsx` | ✓ Exists. `ClientDetailPage` and `ArtistDetailPage` use `<Tabs>`. |

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

~~### #25 — Raw `<select>` with 5-line inline Tailwind class~~
~~**Fixed:** P2 #8 resolved — `SubmitIntakeFormPage` migrated to `<Select>` + `Controller` (2026-06-17). `BookAppointmentForm.tsx` still uses raw `<select>`; tracked separately if needed.~~

~~### #26 — Raw `<textarea>` with 4-line inline Tailwind class~~
~~**Fixed:** P2 #9 resolved — `SubmitIntakeFormPage` migrated to `<Textarea>` and `TEXTAREA_CLS`/`SELECT_CLS` removed (2026-06-17).~~

~~### #27 — Each page builds its own sticky header~~
~~**Fixed:** Layouts own the nav header; pages retain page-level back-nav headers (expected).~~

~~### #28 — IssuerLayout.tsx in wrong directory~~
~~**Fixed:** `layouts/IssuerLayout.tsx` in correct location.~~

~~### #29 — store.ts diverges from frontend.md spec~~
~~**Fixed:** `frontend.md` updated to reflect actual reducers.~~

---

## P6 — Backend Missing Jobs / Hub Design

_All P6 issues resolved (2026-06-17)._

~~### #30 — Only one SignalR hub (ScheduleHub)~~
~~**Fixed:** `DesignHub.cs` and `NotificationHub.cs` created. `RealtimeNotifier` routes by event name: design events → DesignHub, `NotificationReceived` → NotificationHub, all others → ScheduleHub. Hubs mapped at `/hubs/design` and `/hubs/notification`. `useSignalR.ts` opens 3 connections. Frontend event name `DesignRevisionUploaded` corrected to `DesignUploaded` to match backend.~~

~~### #31 — No background job for design revision timeout~~
~~**Fixed:** `DesignApprovalStatus.Expired` added (stored as string, no migration needed). `DesignRevisionTimeoutJob` creates/updates a `DesignApproval` to `Expired` if not already `Approved` or `ChangesRequested`. `IJobScheduler.ScheduleDesignRevisionTimeout` added. `UploadDesignRevisionCommand` schedules a 14-day timeout on each new revision. 7 unit tests added.~~

~~### #32 — No background job for payment reconciliation~~
~~**Fixed:** `PaymentReconciliationJob` runs nightly at 02:00 UTC via Hangfire. Reconciles `Captured` payments where Stripe reports `succeeded` (missed webhook → marks `Paid`). Cancels `Pending` card payments on appointments more than 3 days past (marks `Failed`). 8 unit tests added.~~

---

## P7 — Test Coverage

| # | What | Detail |
|---|---|---|
| 33 | Frontend test coverage ~3% | Only `artists.test.tsx` and `clients.test.tsx` exist. No tests for: auth, appointments, billing, designs, forms, payments, notifications, deposit-rules, store, hooks |
| 34 | No auth flow tests | Login, role routing, token expiry, logout — none tested |
| 35 | No RTK Query endpoint tests | None of the API slices have tests verifying request shape or response handling |
| 36 | No E2E test setup | No Playwright or Cypress configured |
