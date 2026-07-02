# Overnight Prompt — Client Role: Autonomous QA → Bug Fix → Polish Loop
**Date:** 2026-07-01
**Mode:** Fully autonomous. No user present. Run until every loop exits clean.

---

## Your Mission

You are QA engineer and product designer testing the experience of a logged-in tattoo
studio client. The client has the most limited role but the highest emotional stakes —
this is someone paying for a creative service, trusting the studio with their body.
Every confusing screen, broken button, or missing feedback message is a real-world
client who loses confidence and abandons the booking.

Two phases, run in order. Do not skip to Phase 2 until Phase 1 is fully green.

**Phase 1 — Bug Hunt:** Walk every client-accessible screen, front to back. Fix each
bug immediately, re-test, and keep looping until the suite is green.

**Phase 2 — Polish:** Evaluate every screen as if you were a first-time client who
just had their consultation. Implement what a finished tattoo booking app needs.

---

## Constraints (apply everywhere)

- No new npm or NuGet packages.
- No `useEffect` for data fetching. Approved: resize, keyboard, outside-click,
  scroll-to, clipboard, timer side-effects (debounce/setTimeout), browser API
  calls in event handlers, form state sync from async data (e.g. `setValue` when
  `myClient` loads).
- TypeScript strict mode. No `any`. No default exports on components.
- No business logic in endpoints — call MediatR only.
- Every DB query on tenant data through EF Core global query filters.
- Every endpoint has `.RequireAuthorization()` with the correct policy.
  Client endpoints use `ClientAndAbove` policy.
- Never log PII. Serilog logs must include `tenant_id`, `user_id`, `request_id`.
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

## Client Surface Map

The client role lands at `/book` and uses `ClientLayout`. Five nav items:

| Nav Label | Route | Component |
|---|---|---|
| Book Appointment | `/book` | `BookPage` → `BookAppointmentForm` + `MyBookingsSection` |
| My Designs | `/designs` | `DesignListPage` |
| Intake Forms | `/forms/intake` | `IntakeFormListPage` |
| Consent Forms | `/forms/consent` | `ConsentFormListPage` |
| My Profile | `/clients/me` | `MyProfilePage` |

**Additional client-accessible routes:**
```
/designs/:id              DesignDetailPage   (read revisions, approve/request changes)
/forms/intake/new         SubmitIntakeFormPage
/forms/intake/:id         IntakeFormDetailPage  (read own form)
/forms/consent/new        SignConsentFormPage
/forms/consent/:id        ConsentFormDetailPage (read own form)
/pay/:paymentId           DepositCheckoutPage   (Stripe checkout)
/account/change-password  ChangePasswordPage
```

**What clients CANNOT access:**
```
/dashboard, /artists, /artists/:id, /clients, /clients/:id
/deposit-rules, /billing, /studios/me, /payments, /schedule
/appointments/:id
```

The client has NO appointment detail page. They view and manage their bookings
exclusively through `MyBookingsSection` on the `/book` page and `/pay/:paymentId`.

**Backend endpoints (client role — `ClientAndAbove` policy):**
```
POST   /api/v1/appointments             → CreateAppointmentCommand
GET    /api/v1/appointments             → GetAppointmentsQuery (own only — see A3)
GET    /api/v1/appointments/check-slot  → CheckSlotAvailabilityQuery
GET    /api/v1/appointments/mine        → GetMyAppointmentsQuery (dedicated client endpoint)

GET    /api/v1/clients/me               → GetMyClientQuery
GET    /api/v1/clients/me/profile       → GetMyClientProfileQuery
PUT    /api/v1/clients/me/body-map      → UpdateMyBodyMapCommand
PUT    /api/v1/clients/me/portable      → UpdatePortableProfileOptInCommand

GET    /api/v1/designs                  → GetDesignsQuery (own designs only)
GET    /api/v1/designs/:id              → GetDesignQuery
GET    /api/v1/designs/:id/revisions    → GetRevisionsQuery
POST   /api/v1/designs/:id/revisions/:rid/review → ReviewRevisionCommand

GET    /api/v1/forms/intake             → GetIntakeFormsQuery (own)
POST   /api/v1/forms/intake             → SubmitIntakeFormCommand
GET    /api/v1/forms/intake/:id         → GetIntakeFormQuery
GET    /api/v1/forms/consent            → GetConsentFormsQuery (own)
POST   /api/v1/forms/consent            → SignConsentFormCommand
GET    /api/v1/forms/consent/:id        → GetConsentFormQuery

GET    /api/v1/artists                  → GetArtistsQuery (public, for booking form)
GET    /api/v1/deposit-rules            → GetDepositRulesQuery (read, for booking form)
GET    /api/v1/payments/:paymentId/client-secret → GetPaymentClientSecretQuery
POST   /api/v1/payments/cash/declare    → DeclareCashDepositCommand

GET    /api/v1/notifications            → GetNotificationsQuery (own)
PATCH  /api/v1/notifications/preferences → UpdateNotificationPreferencesCommand
```

---

# PHASE 1 — BUG HUNT

## The Loop Algorithm

```
LOOP:
  1. Build:
       cd "Pena e Arte" && dotnet build
       cd frontend && pnpm build          (TypeScript errors surface here)
  2. Test:
       dotnet test --no-build
       pnpm test
  3. Collect every failure.
  4. For each failure:
       a. Read the source file in full.
       b. Diagnose the exact root cause.
       c. Fix only what is broken.
       d. Re-run just that test file to confirm the fix.
       e. If still failing: diagnose from scratch. Fix differently. Re-run.
       f. Repeat until that test is green.
  5. After all fixes: re-run the full suite.
  6. If new failures appeared: go to step 4.
  7. All green → EXIT PHASE 1, ENTER PHASE 2.
```

---

## Audit Checklist — work through in order while fixing failures

### Layer A — Backend: Correctness + Isolation

#### A1. GetMyClient and GetMyClientProfile

Files: `GetMyClientQuery.cs`, `GetMyClientProfileQuery.cs`

These are the client's "self" endpoints. Verify:
- Both filter by `UserId == currentUserId` to return only THIS client's record.
- `GetMyClientQuery` → returns `ClientResponse` including `id`, `firstName`, `lastName`,
  `email`, `phone`. If no client record exists for this user, returns 404 — not a crash.
- `GetMyClientProfileQuery` → returns `ClientProfileResponse` including `bodyMapLocations`,
  `allowCrossTenantRead`. If the profile row doesn't exist yet, returns `bodyMapLocations: []`
  and `allowCrossTenantRead: false` (auto-creates or returns empty defaults — not 404).
- `UpdateMyBodyMapCommand` → persists `bodyMapLocations` to the profile for `UserId == currentUserId`.
  Does NOT accept a `clientId` param (the endpoint is `/clients/me/body-map`).
- `UpdatePortableProfileOptInCommand` → sets `allowCrossTenantRead` for `UserId == currentUserId`.

**Critical check:** The `PUT /clients/me/body-map` handler must look up the client by
`currentUserId` — it must NOT accept a client `id` in the URL body. A client cannot
update another client's body map.

#### A2. GetMyAppointments vs GetAppointments

Two query paths exist for appointments:
- `GET /api/v1/appointments/mine` → `GetMyAppointmentsQuery` — explicitly for the client.
  Returns appointments where `client.UserId == currentUserId`.
- `GET /api/v1/appointments` → `GetAppointmentsQuery` — used by staff (and the booking
  form's slot-check path).

Verify:
- `MyBookingsSection` uses `useGetMyAppointmentsQuery()` — confirm the RTK Query endpoint
  calls `/api/v1/appointments/mine` (not `/api/v1/appointments`).
- If the client somehow calls `GET /api/v1/appointments` with `ClientAndAbove` policy,
  the handler must scope to `client.UserId == currentUserId`. It must NOT return other
  clients' appointments.
- `GetMyAppointmentsQuery` returns ALL of the client's appointments (past and upcoming),
  since `MyBookingsSection` divides them client-side.

#### A3. CreateAppointment — client books themselves

File: `CreateAppointmentCommand.cs`

When a client submits `BookAppointmentForm`, the backend receives:
```json
{ "artistId": "...", "clientId": "...", "date": "...", "durationMinutes": ...,
  "depositRuleId": null, "notes": null }
```

Critical verifications:
- The backend MUST verify that when the caller is `Client` role, the `clientId` in the
  body matches the client record whose `UserId == currentUserId`. A client cannot book
  an appointment for another client by sending a different `clientId`.
  Fix: in the handler, if `ICurrentUser.Role == "Client"`, override the incoming `clientId`
  with the looked-up client's id (or reject if they differ).
- `depositRuleId` is nullable — when null, no deposit is required.
- `date` must be in the future (server-side validation — don't rely solely on the frontend
  `datetime-local` `min` attribute).
- `durationMinutes` must be one of the VALID_DURATIONS: `[30, 45, 60, 90, 120, 180, 240,
  300, 360, 480]`. Validate this server-side.
- On success: returns `AppointmentResponse` including `id`, `depositAmount`, `depositStatus`.
  The frontend uses `depositAmount > 0` to decide whether to show the deposit step.

#### A4. CheckSlotAvailability

File: `CheckSlotAvailabilityQuery.cs`

The booking form debounces a slot check 600ms after the artist/date/duration are all filled.
Verify:
- Checks that the artist has no conflicting appointments at the requested time.
- Checks that the date/time falls within the artist's working hours (if schedule is set).
- Checks that the date is not in a time-off period for the artist.
- Returns `{ available: true }` or `{ available: false, reason: "..." }`.
- The endpoint is accessible with `ClientAndAbove` (unauthenticated clients can't book).
- The `reason` string is shown in the UI as an error — never include internal IDs or PII.

#### A5. SubmitIntakeForm

File: `SubmitIntakeFormCommand.cs`

When a client submits `SubmitIntakeFormPage`, the payload includes:
```json
{ "clientId": "...", "formData": "...", "appointmentId": null, "fileUrl": null }
```

Verify:
- Backend ignores the `clientId` from the request body when called by `Client` role —
  it must derive `clientId` from `ICurrentClient` (looked up by `UserId`). The client
  cannot submit an intake form on behalf of another client.
- `appointmentId` when provided: verify the appointment belongs to the submitting client's
  tenant AND to this client. A client must not be able to attach a form to another
  client's appointment.
- `formData` minimum 10 characters — validate server-side.
- `fileUrl` when provided: validate it's a URL (or null).
- Response: `IntakeFormResponse` with `id`, `submittedAt`, `formData`.

#### A6. SignConsentForm

File: `SignConsentFormCommand.cs`

Verify:
- `clientId` from the body is ignored for `Client` role — derived from `ICurrentClient`.
- `appointmentId` is required and must belong to this client.
- `signatureData` is validated — minimum 2 characters, trimmed.
- On success: a PDF is generated and attached to the appointment record.
  Verify the PDF generation doesn't crash if the `signatureData` contains special characters.
- A client cannot re-sign a consent form for the same appointment.
  If a consent form already exists for this `appointmentId`: return 409 Conflict
  with a helpful message ("You've already signed a consent form for this appointment.").

#### A7. ReviewRevision (client approves/requests changes)

File: `ReviewRevisionCommand.cs`

The client is the ONLY one who can approve or request changes on a design revision.
(Artists must NOT be able to approve their own work on behalf of the client.)

Verify:
- Handler checks that `design.ClientId == ICurrentClient.Id` (not just tenant scope).
- `approved: true` → sets `revision.ApprovalStatus = "Approved"`, `Design.Status = "Approved"`.
- `approved: false` → sets `revision.ApprovalStatus = "ChangesRequested"`, `Design.Status = "ChangesRequested"`.
  Saves `notes` to `revision.ApprovalNotes`.
- Cannot review a revision that is already `Approved` (design is locked).
  Return 409: "This revision has already been approved."
- Cannot review a revision that has been deleted.

#### A8. GetDesigns — client sees only their own

File: `GetDesignsQuery.cs`

When called by a `Client` role:
- Returns only designs where `design.ClientId == ICurrentClient.Id`.
- Does NOT accept a `artistId` filter from clients (they can't filter by artist).
- `Design.Status` filter accepted from the query param.

#### A9. GetIntakeForms / GetConsentForms — client sees only their own

Files: `GetIntakeFormsQuery.cs`, `GetConsentFormsQuery.cs`

Verify:
- Both handlers, when called by `Client` role, add `.Where(f => f.ClientId == clientId)`
  where `clientId` is derived from `ICurrentClient`, NOT from the query param.
- A client cannot pass `?clientId=otherClientId` to see another client's forms.
  For client role: ignore the `clientId` query param, always use `ICurrentClient.Id`.

#### A10. Notification scope for client

File: `GetNotificationsQuery.cs`

Client receives notifications (e.g., appointment confirmed, design ready for review).
Verify:
- Returns only notifications for `UserId == currentUserId`.
- Relevant client notification types:
  - `AppointmentConfirmed` — studio confirmed the booking
  - `AppointmentCancelled` — studio cancelled
  - `DesignRevisionUploaded` — artist uploaded a new design revision
  - `DepositCaptured` — deposit charge has been taken
  - `IntakeFormReceived` — acknowledgement after submitting intake form

---

### Layer B — Frontend State (Client Perspective)

#### B1. BookAppointmentForm — clientId useEffect

The form sets `clientId` from `myClient?.id` via:
```ts
useEffect(() => {
  if (isClientRole && myClient?.id) {
    setValue("clientId", myClient.id);
  }
}, [isClientRole, myClient?.id, setValue]);
```

This is an approved `useEffect` (form state sync from async data). Verify:
- If `myClient` is undefined when the component mounts (still loading), the form
  submits with `clientId: ""`. The schema requires `clientId.min(1)`, so Zod validation
  catches this and shows "Select a client". But this is confusing for a client — they
  shouldn't need to select themselves.
  Fix: in `onSubmit`, derive `clientId` from `myClient?.id ?? values.clientId` (already
  done in the code: `const clientId = isClientRole ? (myClient?.id ?? values.clientId) : values.clientId`).
  Verify this fallback works correctly.
- If `myClient` is null (client user exists in auth but has no `Client` record —
  possible edge case): the form should show a loading state or an error, not silently
  submit with an empty `clientId`. Add a guard:
  ```tsx
  if (!myClient && isClientRole) {
    return (
      <div className="py-6 text-center text-sm text-muted-foreground">
        Setting up your profile… please wait.
      </div>
    );
  }
  ```

#### B2. DepositCheckoutPage — window.location.origin bug

File: `frontend/src/features/payments/components/DepositCheckoutPage.tsx`

Line:
```ts
return_url: `${window.location.origin}/pay/${paymentId}?status=complete`,
```

**Bug:** `window.location.origin` returns the current browser URL's origin, which works
in a browser but is wrong in two cases:
1. If the app is served behind a CDN or reverse proxy, `window.location.origin` may
   return the wrong host.
2. If running in SSR/test environments, `window` is undefined.

Fix: use `import.meta.env.VITE_PUBLIC_URL`:
```ts
const publicUrl = import.meta.env.VITE_PUBLIC_URL ?? window.location.origin;
return_url: `${publicUrl}/pay/${paymentId}?status=complete`,
```

#### B3. MyBookingsSection — no link to appointment details

`BookingRow` renders a booking card with date, artist, status, and deposit area, but
there is NO way for the client to tap/click to view more details about the appointment.
The client has no access to `/appointments/:id` (that route is `ArtistAndAbove`), so
this is by design — BUT the booking row should at minimum show notes if they exist.

Verify `AppointmentResponse` includes a `notes` field. If it does: render notes in
`BookingRow` below the artist/duration line when non-empty.

Also verify: `AppointmentResponse` includes `endDate` (used for the upcoming/past
split in `MyBookingsSection`). If `endDate` is missing from the response type, add
it to `GetAppointmentsHandler` projection:
```ts
EndDate = a.Date.AddMinutes(a.DurationMinutes),
```

#### B4. SubmitIntakeFormPage — appointments dropdown shows cancelled/no-show

The appointment dropdown in `SubmitIntakeFormPage` calls:
```ts
const { data: appointments } = useGetAppointmentsQuery({});
```

This returns ALL of the client's appointments including cancelled and completed ones.
A client should only be able to link an intake form to a PENDING or CONFIRMED appointment.

Fix: filter the dropdown:
```tsx
const relevantAppointments = appointments?.filter(
  (a) => a.status === "Pending" || a.status === "Confirmed"
);
```

Apply the same filter in `SignConsentFormPage` — a client should only sign consent
for pending or confirmed appointments.

Also verify: the same `useGetAppointmentsQuery({})` in `SubmitIntakeFormPage` goes to
`GET /api/v1/appointments` with `ClientAndAbove`. Confirm the backend scopes to the
client's own appointments (see A3).

#### B5. SignConsentFormPage — duplicate consent detection

The backend should reject a second consent form for the same appointment (see A6).
The frontend needs to handle the 409 response:
```ts
const result = await signConsentForm({ ... });
if ("error" in result) {
  const status = (result.error as { status?: number })?.status;
  if (status === 409) {
    toast.error("You've already signed a consent form for this appointment.");
  } else {
    toast.error("Failed to sign consent form.");
  }
}
```

Currently the code only shows a generic error. Fix both frontend and backend to handle
this gracefully.

#### B6. IntakeFormListPage / ConsentFormListPage — missing Submit/Sign CTA

**Bug:** The `IntakeFormListPage` header has NO "Submit new form" button. A client on
this page who wants to submit a new intake form has to navigate manually to
`/forms/intake/new`. There is no affordance.

Same issue for `ConsentFormListPage` — no "Sign consent form" button in the header.

Fix: add action buttons in each page's header:
```tsx
// IntakeFormListPage header:
<Button size="sm" onClick={() => navigate("/forms/intake/new")}>
  Submit intake form
</Button>

// ConsentFormListPage header:
<Button size="sm" onClick={() => navigate("/forms/consent/new")}>
  Sign consent form
</Button>
```

Also fix the empty state copy in `IntakeFormListPage`:
```tsx
// Current (wrong from client perspective):
"Intake forms appear here after clients submit them during booking."
// Fix:
"You haven't submitted any intake forms yet."
```

#### B7. IntakeFormListPage — empty state when forms exist but no match

When `forms.length === 0 && !clientId && !appointmentId`, the client sees an empty
state. The filters `clientId` and `appointmentId` are query params — for a normal
client browsing `/forms/intake`, these will be undefined, so the correct empty state
shows. However the empty state says "Intake forms appear here after clients submit them
during booking" which sounds like the admin copy, not the client copy. Fix (see B6).

Also: the count badge at the top right shows "X forms" — verify this doesn't say
"0 forms" when the list is empty (show nothing instead, or just don't render the badge
when `forms.length === 0`).

#### B8. MyProfilePage — no way to edit contact information

`MyProfilePage` shows the client's name, email, and phone. None of these are editable
from this page. The body map is editable, but there's no "Edit profile" button for
name, email, or phone.

This is a gap (phone in particular) — clients may need to update their phone number.
**Track this for Phase 2 P3** but first check: is there a `PUT /clients/me` endpoint?

Read `clientsApi.ts` and `UpdateMyClientCommand.cs` (or equivalent). If the endpoint
exists, wire up an edit form in Phase 2. If it doesn't exist, create it in Phase 2.

#### B9. BookAppointmentForm — "Book another" doesn't clear slot debounce state

In the confirmation step, clicking "Book another" calls:
```ts
function startOver() {
  setBooked(null);
  setDepositDone(null);
}
```

But `debouncedCheck` state is NOT cleared. When the user starts the form again and
fills in the same artist/date/duration, the debounce timer won't re-trigger because
the values match the existing `debouncedCheck` state. The slot availability check
won't run again.

Fix: clear debounced check state in `startOver`:
```ts
function startOver() {
  setBooked(null);
  setDepositDone(null);
  setDebouncedCheck(null);  // ← add this
}
```

#### B10. ReadOnlyBanner inside ClientLayout

`ClientLayout` renders `<ReadOnlyBanner />`. This component calls
`useGetSubscriptionQuery()`. The subscription endpoint is `OwnerOnly` or
`ArtistAndAbove` — it's not a `ClientAndAbove` endpoint.

Verify: does `ReadOnlyBanner` call the subscription query unconditionally, or does it
skip for client role? If it calls it unconditionally, the client gets a 403 on every
page load, polluting the error log.

Fix: `ReadOnlyBanner` should skip the subscription query when the current role is
`Client`:
```ts
const role = useAppSelector((s) => s.auth.role);
const { data: sub } = useGetSubscriptionQuery(undefined, {
  skip: role !== Role.Owner,
});
```

The `ReadOnlyBanner` should only show for `Owner` role — a client can't do anything
about the studio's subscription. If the studio is in grace period, the `SuspensionBanner`
(which receives `role="client"`) should handle showing a message to the client.

#### B11. SuspensionBanner role="client"

`ClientLayout` renders `<SuspensionBanner role="client" />`. Read `SuspensionBanner.tsx`:
- Verify it handles the `role="client"` prop correctly.
- For the client, the banner should say "This studio is temporarily unavailable" — not
  business details about billing or subscriptions.
- The client should NOT see "Subscribe" or "Reactivate" CTAs — those are owner actions.
- Verify the banner doesn't crash when `role="client"` and the studio is suspended.

---

### Layer C — Frontend Components (Client Perspective)

#### C1. ClientLayout

Verify:
- 5 nav items render correctly: Book Appointment, My Designs, Intake Forms, Consent Forms, My Profile.
- `NotificationBell` in the header renders. Client receives real-time notifications.
- `UserMenu` → logout dispatches action and navigates to `/login`. ✓
- `useSignalR(tenantId)` is called — client gets real-time updates when the studio
  confirms appointments or uploads a new design revision.
- Mobile overflow: "Book Appointment" is a long label. On narrow screens the nav may
  overflow. Add `overflow-x-auto scrollbar-none` to the `<nav>` element.
- `SuspensionBanner` and `ReadOnlyBanner` both in layout header — verify they stack
  cleanly and don't overlap the sticky header.
- No subscriptions or billing priming in `ClientLayout`. ✓

#### C2. BookPage + BookAppointmentForm

Verify the complete booking flow:

**Step 1 — Form:**
- Artist selector: shows all studio artists with avatar + specialization. ✓
- Search within artist dropdown: filters by name and specialization. ✓
- Date/time picker: `min` attribute set to now. ✓
- Session length: 10 valid durations in the dropdown. ✓
- Slot availability: debounces 600ms, shows spinner then green/red indicator. ✓
- Deposit rule selector: only shows when studio has active rules. ✓
  - `DepositPreview` estimates amount from `amountFixed` or `amountPercent × hourlyRate × hours`. ✓
  - Estimated deposit is `null` when artist has no `hourlyRate` and rule is `amountPercent`. 
    Verify: `DepositPreview` returns `null` when `estimated === null` — no NaN displayed. ✓
- Notes field: optional, no max length. **Add a `maxLength` or Zod `max(2000)` to prevent
  very large payloads.**
- Submit button disabled when `slotStatus.available === false`. ✓
- Submit button shows spinner during in-flight. ✓
- On error: toast with server message or fallback. ✓

**Step 2 — Deposit:**
- Only shown when `booked.depositAmount > 0 && isClientRole && !depositDone`. ✓
- `PaymentMethodSelector` shown — verify it offers card and cash options.
- "I'll sort the deposit out later" button: skips and goes to step 3 with `depositDone = "skipped"`. ✓
- **Missing:** If the client skips the deposit and then refreshes the page, the deposit
  state is lost. The booking still exists and the deposit is due. The `MyBookingsSection`
  shows the deposit button again — verify this works.

**Step 3 — Confirmation:**
- Correct icon: `Banknote` for cash, `CheckCircle2` for card or no-deposit. ✓
- Correct message per `depositDone` state. ✓
- "Book another" button: resets form, clears booked state. Fix: also clear `debouncedCheck` (see B9). ✓

#### C3. MyBookingsSection

Verify:
- `useGetMyAppointmentsQuery()` loads. ✓
- Upcoming vs Past split logic: 
  ```ts
  new Date(a.endDate) >= now  →  upcoming
  ```
  Verify `endDate` is present on `AppointmentResponse`. If not, fall back to `date` + `durationMinutes`.
- `DepositArea` states:
  - `depositAmount <= 0` → renders nothing. ✓
  - `Forfeited` → "Deposit forfeited". ✓
  - `Refunded` → "Deposit refunded". ✓
  - `Paid` → green "Deposit paid". ✓
  - `Captured` → green "Deposit authorised — charged when the studio confirms". ✓
  - `CashPending` → "Paying €X in cash at studio" + "Pay by card instead" button. ✓
  - No payment yet / `Pending` → "Pay deposit — €X" button. ✓
- `DepositArea` calls `useGetPaymentByAppointmentQuery(appt.id)`. Verify this endpoint
  exists as `GET /api/v1/payments/appointment/:appointmentId` and is accessible by clients.
  The client must only be able to see their own appointment's payment (not any other payment).
- Loading state per `DepositArea`: spinner while loading. ✓
- Error: no error branch in `DepositArea` — add one:
  ```tsx
  {isError && (
    <p className="text-xs text-muted-foreground">Could not load deposit status.</p>
  )}
  ```

#### C4. DepositCheckoutPage

The full-page Stripe checkout reached at `/pay/:paymentId`.

Verify:
- `VITE_STRIPE_PUBLISHABLE_KEY` is set in env. If empty string: `loadStripe("")` fails
  silently — the Stripe `Elements` don't render. Add a guard:
  ```ts
  if (!import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY) {
    console.error("[Stripe] VITE_STRIPE_PUBLISHABLE_KEY is not set.");
  }
  ```
- `?status=complete` redirect from Stripe: renders `CheckCircle2` success state immediately. ✓
- Loading state for `clientSecret`: spinner. ✓
- Error state (payment not found / no access): `AlertCircle` + descriptive message. ✓
- `CheckoutForm` when `stripe || elements` not ready: submit button disabled. ✓
- On payment error: `errorMsg` banner appears. ✓
- On success (no redirect): `succeeded` state + CheckCircle. ✓
- **Bug (B2 above):** `window.location.origin` in `return_url`. Fix to `VITE_PUBLIC_URL`.
- **Missing:** After `?status=complete`, the client has no link back to the booking page.
  Add: `<Button onClick={() => navigate("/book")}>Back to booking</Button>`.
- Appearance: the Stripe `Elements` use `theme: "stripe"` — in dark mode, this looks
  completely wrong. Fix: detect dark mode and pass `theme: "night"` when appropriate:
  ```ts
  const isDark = document.documentElement.classList.contains("dark");
  appearance: { theme: isDark ? "night" : "stripe" }
  ```

#### C5. MyProfilePage

Verify all three tabs:

**Profile tab:**
- Avatar initials computed from `firstName` + `lastName`. ✓
- Email and phone shown in contact card. ✓
- Body map section: shows `BodyMap` component with `bodyMapLocations`. ✓
- Edit mode: clicking "Edit" sets mode to `"edit"`, copies locations to draft. ✓
- Save: calls `updateMyBodyMap(bodyMapDraft)`. ✓
- Cancel: resets `bodyMapMode` to `"view"`. ✓
- Save button shows spinner while `isSavingMap`. ✓
- **Missing:** No success toast after saving body map. Add:
  ```ts
  async function saveBodyMap() {
    await updateMyBodyMap(bodyMapDraft);
    toast.success("Body map saved.");
    setBodyMapMode("view");
  }
  ```
- **Missing:** No error handling in `saveBodyMap` — if the mutation fails, the UI
  switches to view mode anyway. Fix:
  ```ts
  async function saveBodyMap() {
    const result = await updateMyBodyMap(bodyMapDraft);
    if ("error" in result) {
      toast.error("Failed to save body map.");
      return;
    }
    toast.success("Body map saved.");
    setBodyMapMode("view");
  }
  ```

**Tattoo History tab:**
- Loading skeleton. ✓
- Empty state: "No tattoo history recorded yet." ✓
- Each record shows: `bodyLocation`, `completedAt` (formatted), `description`. ✓
- First photo shown. ✓
- **Bug:** `<img src={record.photoUrls[0]} alt="Tattoo" ...>` — `alt="Tattoo"` is
  not descriptive. Fix: `alt={`Tattoo on ${record.bodyLocation}`}`.
- **Bug:** No `onError` on the `<img>` — if the R2 URL expires, a broken image shows.
  Add:
  ```tsx
  onError={(e) => {
    e.currentTarget.style.display = "none";
  }}
  ```
- **Missing:** If the client has tattoo records from OTHER studios (cross-tenant via
  `allowCrossTenantRead`), `useGetMyTattooRecordsQuery()` might return records from
  multiple studios without attribution. Verify each record has a `studioName` field
  in the response. If it does: show a "From: {studioName}" label on cross-tenant records.

**Sharing tab:**
- `PortableProfileToggle` renders. ✓
- Optimistic toggle: state changes immediately, rolls back on error. ✓
- Warning callout when enabled: amber box explaining what is shared. ✓
- **Missing:** The warning says "Your contact information is never shared" — verify this
  is accurate server-side. When `allowCrossTenantRead = true`, the cross-tenant read
  endpoint must NOT return `email`, `phone`, or `userId`. Only `bodyMapLocations`,
  tattoo records, and basic display name should be readable cross-tenant.

#### C6. DesignListPage (client view)

The client's "My Designs" page. Verify:
- Shows only the client's own designs (backend-filtered, see A8).
- Status badges: Draft, InReview, ChangesRequested, Approved.
- **Client priorities:** `InReview` items need the client's attention — verify they're
  visually prominent (they're waiting for the client to approve or request changes).
- Clicking a design → `/designs/:id`. ✓
- Empty state: "No designs yet." with appropriate copy for a client (not "No designs
  assigned").
- Loading skeleton. ✓
- Error state with retry. ✓

#### C7. DesignDetailPage (client view)

This is where the client approves or requests changes on revisions. Critical workflow.

Verify:
- `canReview = isClientRole` (or more precisely, the design's `clientId == currentClientId`).
  The client can ONLY review designs that are theirs — verify this check exists.
- `isReviewable = canReview && revision.approvalStatus !== "Approved"` — can't re-approve. ✓
- **Approve button:** calls `review({ revisionId, approved: true, notes: null })`. ✓
  Toast: "Design approved." ✓
- **Request changes button:** opens form with `notes` textarea.
  Submits `review({ revisionId, approved: false, notes })`. ✓
  Toast: "Changes requested." ✓
- **Share button:** `ShareDesignButton` — verify it's hidden from the client (only artists
  generate share links). Check `ShareDesignButton.tsx` for a role guard.
  If no role guard: the client would see a "Copy share link" button which they shouldn't control.
- **After approval:** both Approve and Request Changes buttons hidden. "Approved" badge visible. ✓
- **Missing:** When `InReview`, show a clear prompt for the client:
  ```tsx
  {design.status === "InReview" && canReview && latestRevision && !latestRevision.approvalStatus && (
    <div className="rounded-md border border-violet-500/30 bg-violet-500/10 px-3 py-2.5 text-sm">
      Your artist is waiting for your feedback on the latest revision.
    </div>
  )}
  ```
- Loading, error, and empty states on the revisions query.

#### C8. IntakeFormListPage / IntakeFormDetailPage

**List:**
- Client sees only their own intake forms. ✓ (backend-filtered)
- Each row links to `/forms/intake/:id`. ✓
- "Submitted" vs "Draft" badge. ✓
- Empty state copy: fix to client-friendly text (see B6). ✓
- Count badge: hide when count is 0.
- Submit new form button in header (see B6). ✓

**Detail:**
- `IntakeFormDetailPage` should be read-only for the client.
- Shows `formData`, `submittedAt`, `fileUrl` (if present as a link), linked appointment.
- Verify there is no "Edit" button on a submitted form (submitted forms are immutable).

#### C9. ConsentFormListPage / ConsentFormDetailPage

**List:**
- Client sees only their own signed consent forms.
- "Sign consent form" button in header.
- Each row links to `/forms/consent/:id`.
- Verify a "Signed" badge appears for signed forms.

**Detail:**
- Shows signature data, appointment linked, signed date.
- Shows a download link for the generated PDF (`consentForm.pdfUrl`).
  Verify `pdfUrl` is in `ConsentFormResponse`. If not: add it to the backend response.
- Read-only — no ability to re-sign or retract a signed form.

#### C10. SubmitIntakeFormPage — appointment list filter

Already partially covered in B4. Additional verifications:
- If the client has NO upcoming appointments: the dropdown shows only the `__none__`
  option. The helper text changes: "You don't have any upcoming appointments to link this form to."
  (Currently shows "Not linked to an appointment" placeholder which is confusing.)
- `fileUrl` field: a plain URL text input is confusing for non-technical clients.
  Relabel to "Reference link (optional)" with placeholder "Link to a reference image or document URL…"
  and help text "Paste a URL to a photo or document if you have one."

#### C11. SignConsentFormPage — UX improvements

- The "Appointment" dropdown is marked as required. If the client has no eligible
  appointments, the form is stuck. Show a message:
  ```tsx
  {!loadingAppts && !relevantAppointments?.length && (
    <p className="text-xs text-muted-foreground">
      No pending appointments found. Book an appointment before signing a consent form.
    </p>
  )}
  ```
  Disable the submit button in this case.
- "Type your full legal name" — the placeholder text should match what's expected:
  `placeholder="e.g. Jane Marie Smith"` instead of generic "Type your full legal name…"
- After signing: success state provides "Back to booking" → `/book` and "Sign another".
  Verify the success state renders correctly (already in code ✓).
- **Missing:** "View my signed forms" link in the success state:
  ```tsx
  <Button variant="ghost" size="sm" onClick={() => navigate("/forms/consent")}>
    View my forms
  </Button>
  ```

---

### Layer D — Test Suite Completeness

#### D1. ClientLayout.test.tsx

Required tests:
- Renders all 5 nav items
- NotificationBell renders
- UserMenu renders
- `useSignalR` called with tenantId
- Logout dispatches logout and navigates to /login
- Mobile nav has overflow-x-auto class
- SuspensionBanner renders
- ReadOnlyBanner skips subscription query for client role

#### D2. BookPage.test.tsx / BookAppointmentForm.test.tsx

Required tests:
- Renders artist selector with fetched artists
- Artist search filters list
- Date/time field has min attribute set to now
- Slot availability indicator shows spinner, then green/red
- Deposit rule selector only shown when active rules exist
- DepositPreview shows estimated amount correctly
  - Fixed amount: shows fixed amount
  - Percent amount with hourlyRate: shows calculated amount
  - Percent amount without hourlyRate: shows nothing (no NaN)
- Submit button disabled when slot unavailable
- Submit button shows spinner during in-flight
- On success: renders deposit step (when depositAmount > 0)
- On success: renders confirmation step (when depositAmount === 0)
- "I'll sort the deposit out later" skips to confirmation
- "Book another" resets form AND clears debouncedCheck
- Error toast shows server message or fallback
- clientId not editable by client role (no client selector shown)

#### D3. MyBookingsSection.test.tsx

Required tests:
- Upcoming appointments shown before past appointments
- Empty state shows when no appointments at all
- DepositArea: renders "Deposit paid" for Paid status
- DepositArea: renders "Deposit authorised" for Captured status
- DepositArea: renders "Paying in cash" for CashPending
- DepositArea: "Pay by card instead" button in CashPending state
- DepositArea: "Pay deposit" button when no payment exists
- DepositArea: renders nothing when depositAmount = 0
- DepositArea: "Deposit forfeited" message
- DepositArea: "Deposit refunded" message
- Loading state shows spinner
- Error state shows message

#### D4. DepositCheckoutPage.test.tsx

Required tests:
- Renders success state when ?status=complete in query params
- "Back to booking" link present on success state
- Renders loading state while fetching clientSecret
- Renders error state when payment not found
- Return URL uses VITE_PUBLIC_URL not window.location.origin
- Submit button disabled when Stripe not loaded
- Submit button shows spinner during processing
- Error message shown on payment failure
- Stripe Elements appearance uses "night" theme when dark mode is active

#### D5. MyProfilePage.test.tsx

Required tests:
- Initials avatar uses firstName + lastName
- Profile tab: contact info rendered (email, phone)
- Body map: "Edit" button enters edit mode
- Body map: "Cancel" exits edit mode without saving
- Body map: "Save" calls updateMyBodyMap mutation, shows success toast
- Body map: Save failure shows error toast and stays in edit mode
- Tattoo History tab: empty state when no records
- Tattoo History tab: tattoo records rendered with location + date + photo
- Tattoo History tab: broken photo image hidden via onError
- Sharing tab: PortableProfileToggle renders
- Sharing tab: toggle calls updatePortableProfileOptIn mutation
- Sharing tab: warning box shown when enabled

#### D6. IntakeFormListPage.test.tsx (client context)

Required tests:
- "Submit intake form" button navigates to /forms/intake/new
- Empty state uses client-friendly copy
- Form rows link to /forms/intake/:id
- "Submitted" badge on submitted forms
- "Draft" badge on draft forms
- Error state with retry button
- Count badge hidden when forms.length === 0

#### D7. SubmitIntakeFormPage.test.tsx

Required tests:
- Appointment dropdown only shows Pending/Confirmed appointments
- Empty appointment dropdown shows helper message
- formData minimum 10 chars validated
- Submit button disabled during loading
- Success state renders after submission
- "Submit another" resets mutation, not form
- "Back to booking" navigates to /book
- Error shown when mutation fails

#### D8. SignConsentFormPage.test.tsx

Required tests:
- Appointment dropdown only shows Pending/Confirmed appointments
- No eligible appointments: shows message + disables submit
- signatureData minimum 2 chars validated
- Submit button shows spinner during loading
- Success state renders with "View my forms" link
- 409 Conflict shows "already signed" toast, not generic error
- Error state for other failures

#### D9. DesignDetailPage.test.tsx (client context)

Required tests:
- "Approve" button visible when isReviewable
- "Request Changes" button visible when isReviewable
- Approve calls review mutation with approved: true
- Request changes opens notes form, submits with approved: false + notes
- "Approve" and "Request Changes" hidden when revision is already Approved
- "InReview" banner prompt shown when design.status === "InReview"
- ShareDesignButton hidden for client role
- Loading, error, empty states on revisions query

---

## Phase 1 Exit Condition

```
dotnet build   → 0 errors, 0 warnings
pnpm build     → 0 TypeScript errors
dotnet test    → All green
pnpm test      → All green
```

---

# PHASE 2 — POLISH TO FINISHED PRODUCT

The client is a paying customer. They interact with this app around a high-emotion
purchase — a permanent tattoo. Every friction point is amplified. Every moment of
clarity and reassurance builds trust.

Evaluate each screen with the question: "Would a nervous first-time tattoo client feel
confident and guided through this?"

---

## P1. Navigation & Layout Polish

### P1.1 Document titles

Add `useDocumentMeta` (create in `shared/utils/useDocumentMeta.ts` if not present):
```ts
import { useEffect } from "react";
export function useDocumentMeta(title: string) {
  useEffect(() => { document.title = title; }, [title]);
}
```

Required page titles:
```
Book Appointment      "Book — Pena e Artë"
My Designs            "My Designs — Pena e Artë"
Design detail         "{designTitle} — Pena e Artë"
Intake Forms          "Intake Forms — Pena e Artë"
Submit Intake Form    "Submit Intake Form — Pena e Artë"
Consent Forms         "Consent Forms — Pena e Artë"
Sign Consent Form     "Sign Consent Form — Pena e Artë"
My Profile            "My Profile — Pena e Artë"
Deposit Payment       "Deposit Payment — Pena e Artë"
Change Password       "Change Password — Pena e Artë"
```

### P1.2 Mobile nav overflow

`ClientLayout` has 5 nav items. "Book Appointment" is 16 characters. On a 375px screen
with 5 items, this overflows.

Fix:
```tsx
<nav className="ml-6 flex items-center gap-1 overflow-x-auto scrollbar-none shrink min-w-0">
```

Consider shortening the "Book Appointment" label to "Book" on narrow screens via
a responsive approach:
```tsx
<span className="hidden sm:inline">Book Appointment</span>
<span className="sm:hidden">Book</span>
```

### P1.3 Per-route error boundaries

Wrap all client routes in `ErrorBoundary` inside `router.tsx`:
```tsx
{ index: true, element: <ErrorBoundary><BookPage /></ErrorBoundary> }
```
Apply to every client route. Currently no client routes have error boundaries.

### P1.4 Sticky header scroll shadow

The sticky header in `ClientLayout` has no visual separator on scroll — only a
`border-b`. Add a subtle shadow on scroll:
```tsx
// In ClientLayout:
const [scrolled, setScrolled] = useState(false);
useEffect(() => {
  const handler = () => setScrolled(window.scrollY > 0);
  window.addEventListener("scroll", handler, { passive: true });
  return () => window.removeEventListener("scroll", handler);
}, []);
```
```tsx
<header className={cn(
  "flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-20 transition-shadow",
  scrolled && "shadow-sm"
)}>
```

---

## P2. Booking Flow Polish

### P2.1 Artist bio on the booking form

When a client selects an artist from the dropdown, show a brief bio + hourly rate
below the selector:
```tsx
{selectedArtist && (
  <div className="rounded-md bg-muted/40 border border-border/20 px-3 py-2.5 space-y-1">
    {selectedArtist.bio && (
      <p className="text-xs text-muted-foreground leading-relaxed">
        {selectedArtist.bio}
      </p>
    )}
    {selectedArtist.hourlyRate && (
      <p className="text-xs font-medium">
        €{selectedArtist.hourlyRate}/hr
      </p>
    )}
  </div>
)}
```

This gives the client confidence they selected the right artist before submitting.

### P2.2 Notes character counter

The notes `<Textarea>` has no character limit and no counter. Add:
- Zod: `z.string().max(2000).optional()`
- UI counter: `{watchedNotes?.length ?? 0} / 2000` shown below the textarea in a
  muted small font.

### P2.3 Date in the past — clear error message

If the client picks a past date/time, the Zod refine shows "Appointment must be in the
future." The browser's native `datetime-local` input with `min` set prevents this, but
the refine is still needed as a server-side safety net.

Verify: when the `min` attribute date is "now" (computed at render time), and the user
keeps the page open past midnight, `min` becomes stale. Add a `key={today}` to the
`Input` to re-mount it daily — or simply trust Zod validation.

### P2.4 "My bookings" section header count

Add a count to "My bookings" card header:
```tsx
<CardTitle className="text-base flex items-center gap-2">
  <CalendarDays className="h-4 w-4" />
  My bookings
  {upcoming.length > 0 && (
    <span className="ml-auto text-xs font-normal text-muted-foreground">
      {upcoming.length} upcoming
    </span>
  )}
</CardTitle>
```

### P2.5 Cancelled booking context

When an appointment has `status === "Cancelled"`, show a "Cancelled" label in
`BookingRow` with a muted/strikethrough style. Currently just shows the status badge —
but the client might not understand they need to rebook.

Add a message under cancelled bookings:
```tsx
{appt.status === "Cancelled" && (
  <p className="text-xs text-muted-foreground">
    This appointment was cancelled.{" "}
    <button
      className="underline hover:text-foreground"
      onClick={() => window.scrollTo({ top: 0, behavior: "smooth" })}
    >
      Book a new appointment
    </button>
  </p>
)}
```

### P2.6 Confirmation email / next steps

After the booking confirmation (step 3), add an informational section about what happens next:
```tsx
<div className="rounded-md bg-muted/40 border border-border/20 px-3 py-3 space-y-1.5 text-xs text-muted-foreground text-left">
  <p className="font-medium text-foreground text-sm">What happens next</p>
  <p>1. The studio will review and confirm your appointment within 24 hours.</p>
  <p>2. {booked.depositAmount > 0 ? "Your deposit will be captured when the studio confirms." : "No deposit required for this session."}</p>
  <p>3. Your artist may reach out to discuss your design.</p>
</div>
```

---

## P3. My Profile Polish

### P3.1 Edit contact information

A client needs to be able to update their phone number (at minimum). Email changes
should require re-verification. Name changes should be allowed.

Check if `PUT /api/v1/clients/me` exists:
- If YES: add an "Edit" button to the Contact card in `MyProfilePage` that toggles
  an inline edit form with `firstName`, `lastName`, and `phone` fields.
  Email should NOT be editable here (requires verification flow).
  On save: toast "Profile updated." ✓
- If NO: create `UpdateMyClientCommand` with handler:
  ```csharp
  record UpdateMyClientCommand(string FirstName, string LastName, string? Phone)
    : IRequest<ClientResponse>;
  ```
  Endpoint: `PUT /api/v1/clients/me` with `ClientAndAbove` policy.
  Handler: looks up client by `UserId == currentUserId`, updates fields, saves.

### P3.2 Body map: no locations selected state

When the body map has `bodyMapLocations: []` (no locations selected), show an
empty-state prompt:
```tsx
{locations.length === 0 && readOnly && (
  <p className="text-xs text-muted-foreground text-center py-2">
    No body locations marked yet.
  </p>
)}
```

### P3.3 Tattoo history — empty state with context

The "Tattoo History" tab empty state is:
"No tattoo history recorded yet."

This is accurate but unhelpful. Add context:
```tsx
<p className="text-sm font-medium">No tattoo history yet</p>
<p className="text-xs text-muted-foreground">
  Your artist will add your tattoos to your profile after each session.
</p>
```

### P3.4 Sharing tab — make "Portable Profile" concept clear

The `PortableProfileToggle` explains what "Portable Tattoo Profile" is, but only
after the user reads the warning box (when enabled). Add a brief explanation before
the toggle:
```tsx
<p className="text-xs text-muted-foreground">
  When enabled, any certified Pena e Artë artist can view your tattoo history
  before booking a session — no need to explain your existing work every time.
</p>
```

---

## P4. Forms Polish

### P4.1 Intake form: real file upload

The "Attachment URL" field is a text input asking for a URL. Most clients won't have
a publicly accessible URL for their reference images — they'll be on their phone.

Replace the URL input with an actual file upload that goes to R2:
1. Add `POST /api/v1/uploads/intake-attachment` endpoint — streams file to R2, returns URL.
   Policy: `ClientAndAbove`. Max size: 10 MB. Accepted MIME: image/* + application/pdf.
2. In `SubmitIntakeFormPage`: replace `fileUrl` text input with a `FileUploadField`
   component that uploads to the endpoint and populates the URL internally.
3. Show upload progress. On success: show the uploaded file name with a remove button.

### P4.2 Consent form: explain what is being signed

The `SignConsentFormPage` intro text says:
"By signing this consent form you acknowledge the risks and procedures associated with
your tattoo session. Type your full legal name below to provide your digital signature."

Add the studio's actual consent form text above the signature field. This requires:
1. `GET /api/v1/forms/consent/template` endpoint that returns the studio's configured
   consent form text.
2. In `SignConsentFormPage`: load and show the template text in a scrollable `<div>`
   before the signature field:
   ```tsx
   <div className="max-h-64 overflow-y-auto rounded-md border bg-muted/20 px-4 py-3 text-xs leading-relaxed">
     {consentTemplate?.text ?? "Standard tattoo consent form..."}
   </div>
   ```
3. If no template configured: show placeholder text.

If implementing the template is too complex for overnight: at minimum, extract the
consent text to a studio-level setting in `StudioProfilePage` (owner configures it),
and expose it via a `GET /api/v1/studios/current/consent-text` endpoint.

### P4.3 Intake form success message — toast vs state

`SubmitIntakeFormPage` uses `isSuccess` state to show a full-page success screen.
`SignConsentFormPage` uses `toast.success` + stays on the form.

These two patterns are inconsistent. Standardize:
- Intake form: keep the full-page success (it's a significant submission).
- Consent form: use the `isSuccess` full-page pattern too (not just a toast).

The current `SignConsentFormPage` code already has the `isSuccess` full-page return —
verify it's using `isSuccess` from `useSignConsentFormMutation`. ✓ (it does).
No change needed here.

---

## P5. Designs Polish

### P5.1 Design list: "Designs" title is too generic

When the client sees the "My Designs" page, the header says "Designs" (just the `DesignListPage`
default). Change the client-facing title to "My Designs":
```tsx
<span className="font-semibold tracking-tight">My Designs</span>
```

### P5.2 Design detail: client action prominence

When a design is `InReview`, the client must take action (approve or request changes).
This is easy to miss. Make it prominent:
- Add a full-width "Action required" banner at the top of the page when `status === "InReview"`.
- The latest revision's Approve / Request Changes buttons should be in a visually
  distinct card at the top, not buried in the revision list.

### P5.3 Design detail: revision image lightbox

When the client views a design revision image, clicking on it should open a lightbox
(full-screen view). The client needs to be able to see the design clearly before approving.

Add a `<dialog>` or shadcn `<Dialog>` lightbox:
```tsx
const [lightboxUrl, setLightboxUrl] = useState<string | null>(null);

// On image click:
<img
  src={revision.imageUrl}
  onClick={() => setLightboxUrl(revision.imageUrl)}
  className="cursor-zoom-in ..."
  alt={`Design revision v${revision.versionNumber}`}
/>

{lightboxUrl && (
  <Dialog open onOpenChange={() => setLightboxUrl(null)}>
    <DialogContent className="max-w-4xl p-0">
      <img src={lightboxUrl} className="w-full h-auto max-h-[80vh] object-contain" />
    </DialogContent>
  </Dialog>
)}
```

---

## P6. Notification Polish (Client)

### P6.1 Notification links to correct context

Each client notification should link to the relevant page:
- `AppointmentConfirmed` → `/book` (scroll to "My bookings" — no appointment detail for clients)
- `AppointmentCancelled` → `/book`
- `DesignRevisionUploaded` → `/designs/{designId}`
- `DepositCaptured` → `/book`
- `IntakeFormReceived` → `/forms/intake`

Verify `NotificationLogListPage` renders these as `<Link>` or `onClick → navigate()`.

### P6.2 NotificationBell — client relevant events

Verify `NotificationBell` dropdown shows client-relevant notifications in a meaningful way:
- "Your appointment has been confirmed — Tue 15 Jul, 14:00"
- "New design revision is ready for your review"

Generic "New notification" messages are not helpful. If notification `message` is
stored as a templated string, verify the templates are set correctly for client-facing
notification types.

---

## P7. Global Polish Items (Client-Specific)

### P7.1 Toast on every mutation success and error

Audit all client-accessible mutations. Required success toasts:
```
Create appointment:               "Appointment requested."  (already present ✓)
Cash deposit declared:            "We've noted your cash deposit. Bring it to the studio."
Update body map:                  "Body map saved."  (missing — fix in P3.1)
Update portable profile:          "Profile sharing updated."  (add to PortableProfileToggle)
Submit intake form:               (full-page success — ✓)
Sign consent form:                (full-page success — ✓)
Approve design revision:          "Design approved."  (already present ✓)
Request design changes:           "Changes requested."  (already present ✓)
Change password:                  "Password changed."  (verify)
```

Error toasts for every mutation failure — not just "Failed" but `error.data?.message ?? "Fallback"`.

### P7.2 Spinner + disabled on every in-flight button

Audit every `<Button>` used by client components. None should be clickable while
their mutation is in-flight. Verify: `BookAppointmentForm`, `DepositCheckoutPage`,
`PortableProfileToggle`, `MyProfilePage` (Save body map), `SubmitIntakeFormPage`,
`SignConsentFormPage`.

### P7.3 Error states with retry on every query

Every RTK Query result in client components must have an `isError` branch:
```tsx
{isError && (
  <p role="alert" className="text-sm text-destructive py-4">
    Failed to load {dataType}.{" "}
    <button className="underline" onClick={refetch}>Try again</button>
  </p>
)}
```
Check: `MyBookingsSection`, `DesignListPage`, `DesignDetailPage`, `IntakeFormListPage`,
`ConsentFormListPage`, `MyProfilePage` (client + profile + tattoos queries).

### P7.4 Empty state on every list view

Every list used by the client must have an empty state:
- `DesignListPage`: "No designs yet. Your artist will create a design project for you."
- `IntakeFormListPage`: "You haven't submitted any intake forms yet." + Submit CTA.
- `ConsentFormListPage`: "No consent forms signed yet." + Sign CTA.

### P7.5 Accessible images throughout

Every `<img>` rendered on client-facing pages must have a descriptive `alt`:
- Portfolio/artist avatars: `alt="${artist.firstName} ${artist.lastName}"`
- Design revision images: `alt={`Design revision v${revision.versionNumber}`}`
- Tattoo history photos: `alt={`Tattoo on ${record.bodyLocation}`}`

---

## Phase 2 Exit Condition

After all polish items:

1. `pnpm test` — all green.
2. `dotnet test` — all green.
3. `pnpm build` — no TypeScript errors.
4. `dotnet build` — no warnings.
5. Self-review checklist — walk every client page and confirm:
   - Document title set?
   - Loading skeleton on every query?
   - Error state with retry on every query?
   - Empty state on every list?
   - Success toast on every mutation?
   - Error toast on every mutation failure?
   - Spinner + disabled on every in-flight button?
   - Error boundaries on every route?
   - Mobile nav doesn't overflow?
   - Client cannot access other clients' data (spot-check in tests)?
   - `window.location.origin` → `VITE_PUBLIC_URL` (spot-check `DepositCheckoutPage`)?
   - Consent form shows consent text before signature?
   - Design revision images have lightbox?

---

## Final Deliverable

When both phases exit cleanly, append to `docs/claude/architecture.md`:

```markdown
## Client QA Pass — 2026-07-01

### Bugs fixed
- [list each bug: file → root cause → fix]

### Polish implemented
- [list each item: component → what was added]

### Architecture decisions
- [any decisions made → copy to Decisions Log table]

### Deferred items
- [anything not done and why]
```
