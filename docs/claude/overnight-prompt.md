# Overnight Master Prompt

> Paste everything between the START and END markers into Claude Code.
> Run with: `claude --dangerously-skip-permissions`
> Commit current work first: `git add -A && git commit -m "chore: pre-overnight checkpoint"`
> Branch: `git checkout -b fix/overnight-session-1`
> Note: No Docker required — backend integration tests are skipped (unit tests only).

---

<!-- ==================== START PASTE HERE ==================== -->

Read CLAUDE.md, then read docs/claude/architecture.md, docs/claude/backend.md,
docs/claude/frontend.md, and docs/claude/conventions.md in full before writing
a single line of code. Do not skip this step.

Work through each section below IN ORDER. After every section, run `pnpm test`
(and `dotnet test --project tests/Pena_e_Arte.UnitTests` where indicated). If tests fail, fix them before moving on.
Do not start the next section until the current one is green.

---

## SECTION 1 — P0 Bug Fixes

These are broken right now. Fix them first.

### Fix 1 — DesignRevisionUploaded invalidates wrong cache
File: `frontend/src/shared/hooks/useSignalR.ts`

The `DesignRevisionUploaded` SignalR event handler (around line 27) currently calls:
```
dispatch(appointmentsApi.util.invalidateTags(["Appointment"]))
```
Change it to:
```
dispatch(designsApi.util.invalidateTags(["Design"]))
```
Import `designsApi` if not already imported. Run `pnpm test` after.

---

### Fix 2 — Subscription status string casing mismatch
File: `frontend/src/features/dashboard/components/DashboardPage.tsx`

Around line 61, status strings are checked in PascalCase (`"Trialing"`, `"GracePeriod"`,
`"PastDue"`, `"Cancelled"`). Open `Pena_e_Arte.Infrastructure` and find the JSON
serialization options for `SubscriptionStatus` enum. Confirm the exact strings the
backend emits, then update the frontend checks to match exactly.
If the backend uses `snake_case` (e.g. `"grace_period"`), update the frontend.
If the backend uses camelCase (e.g. `"gracePeriod"`), update the frontend.
Add a comment above the checks: `// Must match SubscriptionStatus JSON output from backend`

---

### Fix 3 — JWT double-decoded in authSlice
Files:
- `frontend/src/shared/types/jwt.ts` (or wherever `JwtClaims` is defined)
- `frontend/src/features/auth/authSlice.ts:19–22`

Step 1: Open `jwt.ts` and add `exp?: number` to the `JwtClaims` interface/type.
Step 2: In `authSlice.ts`, remove the second decode
(`JSON.parse(atob(token.split(".")[1]))`). The `exp` field is already available on
the result of `decodeToken(token)`. Use that single decoded result for both claims
extraction and expiry check.

Run `pnpm test` after all three fixes.

---

## SECTION 2 — P1 Navigation Layouts

The three missing layouts are the highest-leverage fix in the codebase.
They unblock client/artist navigation, fix the ReadOnlyBanner placement,
and make route guards meaningful.

### Step 1 — Move IssuerLayout to the correct directory
Current location: `frontend/src/features/platform/components/IssuerLayout.tsx`
Correct location: `frontend/src/layouts/IssuerLayout.tsx`

Move the file. Update every import that references the old path.
Do not change the component itself.

### Step 2 — Create ClientLayout.tsx
File: `frontend/src/layouts/ClientLayout.tsx`

Match the visual structure of `IssuerLayout.tsx` (sticky top nav bar).
Navigation links for the client role:
- Book Appointment → `/book`
- My Designs → `/designs`
- Intake Forms → `/forms/intake`
- Consent Forms → `/forms/consent`
- My Profile → `/clients/me`

Include `<ReadOnlyBanner />` inside the layout (import from
`shared/components/ReadOnlyBanner.tsx`). Remove any inline `<ReadOnlyBanner />`
from `BookPage.tsx` or any other client-role page.

### Step 3 — Create ArtistLayout.tsx
File: `frontend/src/layouts/ArtistLayout.tsx`

Navigation links for the artist role:
- Schedule → `/schedule`
- Clients → `/clients`
- Designs → `/designs`
- Intake Forms → `/forms/intake`
- Consent Forms → `/forms/consent`
- Deposit Rules → `/deposit-rules`
- Notifications → `/notifications`

Include `<ReadOnlyBanner />`. Remove inline placement from `SchedulePage.tsx`.

### Step 4 — Create OwnerLayout.tsx
File: `frontend/src/layouts/OwnerLayout.tsx`

Navigation links for the owner role:
- Dashboard → `/dashboard`
- Artists → `/artists`
- Clients → `/clients`
- Designs → `/designs`
- Payments → `/payments`
- Billing → `/billing`
- Studio Settings → `/studios/me`
- Notifications → `/notifications`

Include `<ReadOnlyBanner />`. Remove inline placement from `DashboardPage.tsx`.

### Step 5 — Wire layouts into the router
File: `frontend/src/app/router.tsx`

Wrap each role's routes with its layout:
- Client routes → `<ClientLayout />`
- Artist routes → `<ArtistLayout />`
- Owner routes → `<OwnerLayout />`
- Issuer routes → `<IssuerLayout />`

Do not change the routes themselves — only add the layout wrapper.

Run `pnpm test` after layouts are wired.

---

## SECTION 3 — P2 Shared UI Components

Add each missing shadcn/ui component. Use the shadcn/ui CLI pattern — do not write
components from scratch. After adding ALL components, replace raw HTML usages in
consuming files (listed below each component). Run `pnpm lint` after each
replacement sweep.

All files go in: `frontend/src/shared/components/ui/`

### select.tsx
Use the shadcn/ui Select primitive.
Replace raw `<select>` in:
- `BookAppointmentForm.tsx` — artist and client dropdowns
- `CreateArtistPage.tsx`
- `CreateClientPage.tsx`
- `CreateDepositRulePage.tsx`
Delete any `selectClass` constant that was duplicating shadcn Input styling.

### textarea.tsx
Use the shadcn/ui Textarea primitive.
Replace raw `<textarea>` in:
- `BookAppointmentForm.tsx` — notes field (lines ~169–177)

### badge.tsx
Use the shadcn/ui Badge primitive.
Replace custom inline spans in:
- `AppointmentStatusBadge.tsx`
- `DepositStatusBadge.tsx`

### skeleton.tsx
Use the shadcn/ui Skeleton primitive.
Add skeleton loading states to every list page that currently shows a `Loader2`
spinner: `ClientListPage`, `ArtistListPage`, `DesignListPage`, `PaymentListPage`,
`IntakeFormListPage`, `ConsentFormListPage`, `NotificationLogListPage`.

### dialog.tsx
Use the shadcn/ui Dialog primitive.
Add confirmation dialogs before destructive actions:
- Cancel appointment (in `AppointmentDetailPage` or wherever cancel is triggered)
- Delete design revision
- Delete tattoo record (`TattooHistorySection.tsx`)

Dialog pattern: title + description + Cancel button + Confirm button.
Confirm button is red (destructive variant). Only call the mutation on Confirm.

### toast.tsx / Sonner
Use the shadcn/ui Sonner integration (`sonner` is already in the stack).
Add `<Toaster />` to the root layout or `main.tsx`.
Add `toast.success("...")` / `toast.error("...")` calls after every RTK Query
mutation's `onSuccess`/`onError`. Minimum coverage: create/cancel appointment,
sign consent form, approve/reject design, create client, create artist.

### table.tsx / DataTable
Use the shadcn/ui Table primitives.
Create a generic `DataTable` component in `shared/components/DataTable.tsx`
(not in `ui/` — it is a composed component).
Migrate the following list pages from stacked-card layout to table layout:
- `ClientListPage.tsx`
- `ArtistListPage.tsx`
- `PaymentListPage.tsx`

### avatar.tsx
Use the shadcn/ui Avatar primitive.
Apply to `ArtistCard.tsx` and `ClientCard.tsx`.

### separator.tsx
Use the shadcn/ui Separator primitive.
Apply where section dividers are currently implemented with `<hr>` or
`border-b` margin hacks.

### tabs.tsx
Use the shadcn/ui Tabs primitive.
Apply to:
- `ClientDetailPage.tsx` — tabs: Profile, Tattoo History, Forms, Appointments
- `ArtistDetailPage.tsx` — tabs: Profile, Schedule, Designs

Run `pnpm test` after the full component sweep.

---

## SECTION 4 — P3 Missing Hooks and Slices

### Create useCurrentUser hook
File: `frontend/src/shared/hooks/useCurrentUser.ts`

```typescript
export function useCurrentUser() {
  return useAppSelector((s) => s.auth.user);
}
```

After creating it, find every inline `useAppSelector((s) => s.auth.user)` call
in components and replace with `useCurrentUser()`.

### Create notificationsSlice.ts
File: `frontend/src/features/notifications/notificationsSlice.ts`

State shape:
```typescript
interface NotificationsState {
  unreadCount: number;
  isInboxOpen: boolean;
}
```
Actions: `incrementUnread`, `clearUnread`, `setUnreadCount(number)`, `toggleInbox`.

Add `notificationsReducer` to `app/store.ts`.

### Wire sessionExpired to logout
Files:
- `frontend/src/app/baseQuery.ts` (or wherever the RTK Query baseQuery is defined)
- `frontend/src/app/router.tsx` or a top-level component

In `baseQuery.ts`, when a response returns 401:
1. Dispatch `setSessionExpired(true)` from `uiSlice`.
2. Dispatch `logout()` from `authSlice`.

In `router.tsx` or `App.tsx`, add a listener:
```typescript
const sessionExpired = useAppSelector((s) => s.ui.sessionExpired);
useEffect(() => {
  if (sessionExpired) {
    dispatch(setSessionExpired(false));
    navigate("/login");
  }
}, [sessionExpired]);
```

Run `pnpm test` after.

---

## SECTION 5 — P4 Route and Access Control Gaps

File: `frontend/src/app/router.tsx`

### Fix 1 — /dashboard restricted to owner and issuer only
Wrap the `dashboard` route with a role guard that allows only `["owner", "issuer"]`.
Use the existing `<RoleGuard>` component. Do not change the page component itself.

### Fix 2 — /schedule restricted to artist and issuer only
Wrap the `schedule` route with a role guard that allows only `["artist", "issuer"]`.

### Fix 3 — /book restricted to client and issuer only
Wrap the `book` route with a role guard that allows only `["client", "issuer"]`.

After all three guards, verify the layout wrapping from Section 2 is still intact.
Run `pnpm test` after.

---

## SECTION 6 — SP-01 Platform Branding Flag

This is a new feature. Follow the full "Adding a New Feature" checklist in
docs/claude/architecture.md before starting.

### Backend changes

1. Domain — `Pena_e_Arte.Domain/Entities/Studio.cs`:
   Add `public bool ShowPlatformBranding { get; private set; } = true;`
   Add method `public void UpdateBranding(bool show) => ShowPlatformBranding = show;`

2. Domain — `Pena_e_Arte.Domain/Entities/Plan.cs`:
   Add `public bool AllowBrandingRemoval { get; private set; } = false;`

3. Application — new command:
   `Pena_e_Arte.Application/Studios/Commands/UpdateStudioBrandingCommand.cs`
   `UpdateStudioBrandingCommand(Guid StudioId, bool ShowBranding) : IRequest<Unit>`
   Handler: fetch Studio, fetch Studio's active Subscription → Plan.
   If `!plan.AllowBrandingRemoval && !command.ShowBranding`
   → throw `BusinessRuleViolationException("Your current plan does not allow removing platform branding.")`.
   Otherwise call `studio.UpdateBranding(command.ShowBranding)` and save.

4. Application — add `ShowPlatformBranding` to the `StudioResponse` DTO in
   `Pena_e_Arte.Contracts/Responses/`.

5. Infrastructure — add migration:
   `ShowPlatformBranding` (bool, NOT NULL, DEFAULT 1) to `Studios` table.
   `AllowBrandingRemoval` (bool, NOT NULL, DEFAULT 0) to `Plans` table.
   Run: `dotnet ef migrations add AddPlatformBrandingFlag --project Pena_e_Arte.Infrastructure --startup-project Pena_e_Arte.API`

6. API — add to `Pena_e_Arte.API/Endpoints/StudioEndpoints.cs`:
   `PATCH /api/v1/studios/{id}/branding` → `UpdateStudioBrandingCommand`
   `.RequireAuthorization("OwnerOnly")`

7. FluentValidation — create `UpdateStudioBrandingValidator.cs`:
   Validate `StudioId` is not empty. `ShowBranding` is a bool — no rule needed.

### Frontend changes

8. `frontend/src/features/studios/components/BrandingSettingsCard.tsx` (new file):
   Toggle switch: "Show 'Powered by Pena e Artë' on booking widget and emails."
   If `!studio.plan.allowBrandingRemoval`: disable the toggle, show tooltip
   "Upgrade your plan to remove platform branding."
   Uses `useUpdateStudioBrandingMutation` (add to `studiosApi.ts`).
   Show `toast.success("Branding settings saved.")` on success.

9. `frontend/src/features/booking/components/BookingWidget.tsx`:
   Read `studio.showPlatformBranding` from the RTK Query studio response.
   If true, render at the bottom:
   ```
   <footer>
     <a href="https://penaearte.com" target="_blank" rel="noopener noreferrer">
       Powered by Pena e Artë
     </a>
   </footer>
   ```
   Tailwind only. No inline styles.

### Tests
- Unit test (`Pena_e_Arte.UnitTests`): `UpdateStudioBrandingHandler` —
  assert free-plan studio cannot disable branding (throws `BusinessRuleViolationException`).
  Assert paid-plan studio can toggle branding.
- Integration test (`Pena_e_Arte.IntegrationTests`): PATCH endpoint returns 403
  for artist role. Returns 200 for owner role.

Run `dotnet test --project tests/Pena_e_Arte.UnitTests && pnpm test` after SP-01.
(Integration tests require Docker/MySQL — skip them here, run manually when Docker is available.)

---

## STOP HERE

Do not proceed to SP-02 or any P5/P6/P7 items without human review.
Commit all work: `git add -A && git commit -m "fix/feat: overnight session 1 — P0 bugs, layouts, UI components, hooks, route guards, SP-01"`

<!-- ==================== END PASTE HERE ==================== -->
