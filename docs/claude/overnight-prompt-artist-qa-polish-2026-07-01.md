# Overnight Prompt — Artist Role: Autonomous QA → Bug Fix → Polish Loop
**Date:** 2026-07-01
**Mode:** Fully autonomous. No user present. Run until every loop exits clean.

---

## Your Mission

You are the first QA engineer testing the tattoo artist's experience on this platform.
Artists are the primary service providers — they use this app all day, every day.
Every bug in their workflow costs real appointments.

Two phases, run in order. Do not skip to Phase 2 until Phase 1 is fully green.

**Phase 1 — Bug Hunt:** Walk every artist-accessible screen systematically.
Fix each bug immediately, re-test until green, then move on.

**Phase 2 — Polish:** Evaluate every artist-facing screen as a product manager would.
Decide what a real-world tattoo artist needs, implement what's missing, and run the
full test suite again.

---

## Constraints (identical to all overnight prompts)

- No new npm or NuGet packages.
- No `useEffect` for data fetching. Approved: resize, keyboard, outside-click,
  scroll-to, clipboard, timer side-effects, browser API calls in event handlers.
- TypeScript strict mode. No `any`. No default exports on components.
- No business logic in endpoints — call MediatR only.
- Every DB query on tenant data through EF Core global query filters.
- Every endpoint has `.RequireAuthorization()` with the correct policy.
- Never log PII. Serilog logs must always include `tenant_id`, `user_id`, `request_id`.
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

## Artist Surface Map

The artist role lands at `/schedule` and uses `ArtistLayout`. Nav items:

| Nav Label | Route | Component |
|---|---|---|
| Schedule | `/schedule` | `SchedulePage` |
| Clients | `/clients` | `ClientListPage` |
| Designs | `/designs` | `DesignListPage` |
| Intake Forms | `/forms/intake` | `IntakeFormListPage` |
| Consent Forms | `/forms/consent` | `ConsentFormListPage` |
| Deposit Rules | `/deposit-rules` | `DepositRuleListPage` |
| Notifications | `/notifications` | `NotificationLogListPage` |
| My Portfolio | `/artists/:myId` | `ArtistDetailPage` (own profile) |

**Additional artist-accessible routes** (not in primary nav):
```
/appointments/:id      AppointmentDetailPage   (confirm, complete, no-show)
/artists/:id           ArtistDetailPage        (read other artists; own profile)
/clients/new           CreateClientPage        (artist can add clients)
/clients/:id           ClientDetailPage        (client profile, body map, tattoos)
/clients/:id/tattoos/:tattooId  TattooRecordDetailPage
/designs/new           CreateDesignPage        (artist creates design project)
/designs/:id           DesignDetailPage        (revisions, approvals)
/designs/:id/upload    UploadRevisionPage      (artist uploads revision image)
/forms/intake/:id      IntakeFormDetailPage    (read submitted intake form)
/forms/consent/:id     ConsentFormDetailPage   (read signed consent form)
/deposit-rules/:id     DepositRuleDetailPage   (read deposit rule)
/pay/:paymentId        DepositCheckoutPage     (shared; artist may need to link client)
/account/change-password  ChangePasswordPage
```

**What the artist CANNOT do** (owner-only):
```
/artists/new           (cannot create other artists)
/deposit-rules/new     (cannot create deposit rules)
/deposit-rules/:id     (cannot edit or delete deposit rules)
/billing               (no billing access)
/studios/me            (cannot edit studio settings)
```

The `canManage` flag (`usePermission(Role.Owner)`) controls owner-only actions inside
shared components. Verify every restricted action checks this flag.

**Backend endpoints (artist role context — `ArtistAndAbove`):**
```
GET    /api/v1/artists/me                   → GetMyArtistQuery
GET    /api/v1/artists                      → GetArtistsQuery
GET    /api/v1/artists/{id}                 → GetArtistQuery
PUT    /api/v1/artists/{id}                 → UpdateArtistCommand  (own profile only —
                                              verified in handler by comparing userId)
PUT    /api/v1/artists/{id}/portfolio-images → UpdateArtistPortfolioCommand (own portfolio)
GET    /api/v1/artists/{id}/schedule        → GetArtistScheduleQuery
PUT    /api/v1/artists/{id}/schedule        → UpsertArtistScheduleCommand (own schedule)
POST   /api/v1/artists/{id}/time-off        → AddArtistTimeOffCommand (own time-off)
DELETE /api/v1/artists/{id}/time-off/{id}   → DeleteArtistTimeOffCommand (own time-off)

GET    /api/v1/appointments                 → GetAppointmentsQuery (own appointments)
GET    /api/v1/appointments/{id}            → GetAppointmentQuery
PATCH  /api/v1/appointments/{id}/confirm    → ConfirmAppointmentCommand
PATCH  /api/v1/appointments/{id}/cancel     → CancelAppointmentCommand
PATCH  /api/v1/appointments/{id}/complete   → CompleteAppointmentCommand
PATCH  /api/v1/appointments/{id}/no-show    → MarkNoShowCommand
POST   /api/v1/appointments/{id}/reschedule → RescheduleAppointmentCommand

GET    /api/v1/clients                      → GetClientsQuery
GET    /api/v1/clients/{id}                 → GetClientQuery
POST   /api/v1/clients                      → CreateClientCommand
GET    /api/v1/clients/{id}/profile         → GetClientProfileQuery
GET    /api/v1/clients/{id}/tattoos         → GetTattooRecordsQuery
POST   /api/v1/clients/{id}/tattoos         → AddTattooRecordCommand
GET    /api/v1/clients/{id}/tattoos/{tid}   → GetTattooRecordQuery
PUT    /api/v1/clients/{id}/tattoos/{tid}   → UpdateTattooRecordCommand
DELETE /api/v1/clients/{id}/tattoos/{tid}   → DeleteTattooRecordCommand

GET    /api/v1/designs                      → GetDesignsQuery
GET    /api/v1/designs/{id}                 → GetDesignQuery
POST   /api/v1/designs                      → CreateDesignCommand
POST   /api/v1/designs/{id}/revisions       → UploadRevisionCommand
POST   /api/v1/designs/{id}/share-token     → CreateDesignShareTokenCommand
DELETE /api/v1/designs/{id}/share-token     → RevokeDesignShareTokenCommand

GET    /api/v1/payments                     → GetPaymentsQuery (all, filtered by artist)
POST   /api/v1/payments/cash/confirm        → ConfirmCashDepositCommand

GET    /api/v1/forms/intake                 → GetIntakeFormsQuery
GET    /api/v1/forms/intake/{id}            → GetIntakeFormQuery
GET    /api/v1/forms/consent                → GetConsentFormsQuery
GET    /api/v1/forms/consent/{id}           → GetConsentFormQuery

GET    /api/v1/deposit-rules                → GetDepositRulesQuery
GET    /api/v1/deposit-rules/{id}           → GetDepositRuleQuery

GET    /api/v1/notifications                → GetNotificationsQuery
PATCH  /api/v1/notifications/preferences    → UpdateNotificationPreferencesCommand

GET    /api/v1/appointments/{id}/calendar.ics → GetAppointmentIcsQuery (ICS download)
```

---

# PHASE 1 — BUG HUNT

## The Loop Algorithm

```
LOOP:
  1. Build:
       cd "Pena e Arte" && dotnet build
       cd frontend && pnpm build      (TypeScript errors surface here)
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

### Layer A — Backend: Authorization + Correctness

#### A1. Artist-scope enforcement on artist mutations

The most critical rule: an artist must ONLY be able to modify their OWN profile,
portfolio, schedule, and time-off. Owners can modify any artist in their tenant.

Read `UpdateArtistCommand.cs`, `UpdateArtistPortfolioCommand.cs`,
`UpsertArtistScheduleCommand.cs`, `AddArtistTimeOffCommand.cs`,
`DeleteArtistTimeOffCommand.cs`.

For each, verify:
- The handler reads `ICurrentUser.UserId` (not just `ICurrentTenant`).
- If the caller is `Artist` role, it checks `artist.UserId == currentUserId`.
  If the IDs don't match → returns 403 Forbidden.
- If the caller is `Owner` or `Issuer` → no restriction (allowed to manage any artist).
- Never compare by `artistId` alone — an artist user can guess another artist's GUID.

**Common bug:** Handler only checks tenant scope (via global query filter) but not
`userId` scope for the Artist role. A malicious artist could update a colleague's
portfolio by guessing their GUID.

#### A2. GetMyArtist endpoint

File: `Pena_e_Arte.Application/Artists/Queries/GetMyArtistQuery.cs`
Route: `GET /api/v1/artists/me`

Verify:
- Returns the `Artist` entity whose `UserId == currentUserId`.
- If no artist record exists for this user (e.g., owner who hasn't been set up as
  artist), returns 404 (not a crash).
- Response includes: all `ArtistResponse` fields including `slug`, `specializations`,
  `hourlyRate`, `avatarUrl`, `portfolioImages`.
- `ArtistLayout` calls `useGetMyArtistQuery()` to conditionally render the
  "My Portfolio" nav link. If this returns 404, the nav link must NOT crash —
  verify the frontend query has `skip: !data` or handles the 404 gracefully.

#### A3. Artist schedule — read + write

Files: `GetArtistScheduleQuery.cs`, `UpsertArtistScheduleCommand.cs`,
       `AddArtistTimeOffCommand.cs`, `DeleteArtistTimeOffCommand.cs`

Verify `GetArtistScheduleQuery`:
- Returns `{ schedule: ArtistScheduleDay[], timeOff: ArtistTimeOff[] }`.
- `ArtistScheduleDay`: `{ dayOfWeek: 0-6, startTime: "HH:mm", endTime: "HH:mm" }`.
- `timeOff`: `{ id, start: ISO, end: ISO }`.
- Returns empty arrays if no schedule configured — NOT 404.

Verify `UpsertArtistScheduleCommand`:
- Replaces entire schedule for this artist.
- Validates `startTime < endTime` for each day entry.
- Validates no two entries share the same `dayOfWeek`.
- Artist-scope check: artist must own this schedule (see A1).

Verify `AddArtistTimeOffCommand`:
- `start < end` validated.
- `start > now` validated (can't add time-off in the past).
- Overlap check: no two time-off periods for the same artist can overlap.
  If overlap: returns 409 with a clear message.

Verify `DeleteArtistTimeOffCommand`:
- Returns 404 if time-off entry not found.
- Artist-scope check: artist can only delete their own time-off.

#### A4. Appointment filtering by artist

Files: `GetAppointmentsQuery.cs`

The `SchedulePage` calls `GetAppointmentsQuery({ from, to })` for a week range.
For an artist, this must return ONLY their own appointments, not all studio appointments.

Verify:
- When the caller is `Artist` role: handler adds `.Where(a => a.ArtistId == myArtistId)`.
  The handler should look up `myArtistId` from `GetMyArtistQuery` or from a direct
  `ICurrentArtist` service.
- When the caller is `Owner` or `Issuer`: returns all studio appointments (no artist filter).
- The handler must NOT accept `artistId` as a query param from artists (they can't filter
  by other artists). Only owners can filter by `artistId`.

**Critical bug:** If `GetAppointmentsQuery` returns ALL studio appointments to the artist,
the artist sees appointments that aren't theirs. This is a data leak within the tenant
(different from cross-tenant, but still a privacy issue between colleagues).

#### A5. Design artist-scope

Files: `GetDesignsQuery.cs`, `CreateDesignCommand.cs`, `UploadRevisionCommand.cs`,
       `ReviewRevisionCommand.cs`, `CreateDesignShareTokenCommand.cs`,
       `RevokeDesignShareTokenCommand.cs`, `DeleteRevisionCommand.cs`

Verify `GetDesignsQuery`:
- For `Artist` role: returns designs where `ArtistId == myArtistId`.
- Accepts optional `status` filter (applied after artist scope).
- Accepts optional `clientId` filter (for viewing a specific client's designs).
- Optional `artistId` filter is owner-only.

Verify `CreateDesignCommand`:
- Sets `ArtistId = myArtistId` automatically from `ICurrentArtist`.
- Does NOT allow the artist to set a different `ArtistId`.
- Validates `clientId` belongs to this tenant.

Verify `UploadRevisionCommand`:
- Validates that the design's `ArtistId == myArtistId` (artist-scope check).
- Creates a `DesignRevision` entity with the uploaded `ImageUrl`.
- Transitions `Design.LatestRevisionUrl = imageUrl`.
- Status transition: `Draft → InReview` on first upload; `ChangesRequested → InReview`
  on subsequent uploads.

Verify `ReviewRevisionCommand` (`approve` / `requestChanges`):
- This is the CLIENT's action. Must NOT be callable by artists except on their own
  designs as a workaround. Verify the policy is `ClientAndAbove` and that the backend
  checks the revision belongs to the right client.
  - If artist is also the client of a design (test case), the check should still work.
  - An artist must NOT be able to approve their own design revisions on behalf of the
    client (that would circumvent the approval workflow).

Verify `CreateDesignShareTokenCommand`:
- Creates `DesignShareToken` where the token links to a specific `DesignRevision`.
- Only one active (non-expired, non-revoked) token per design.
- If an active token already exists: return it (don't create a duplicate).
- Artist-scope: only the design's artist can generate a share token.

Verify `DeleteRevisionCommand`:
- Validates revision belongs to a design owned by the requesting artist.
- Cannot delete the only revision (design would have no `LatestRevisionUrl`).
  If deleting leaves 0 revisions, also clear `Design.LatestRevisionUrl`.
- Cannot delete a revision that has been approved.

#### A6. Cash confirmation scope

File: `ConfirmCashDepositCommand.cs`

An artist can confirm cash received from a client. This is common — the artist is the
one physically receiving the deposit before an appointment.

Verify:
- Artist can confirm cash on appointments where `ArtistId == myArtistId`.
- Artist CANNOT confirm cash on another artist's appointments.
- Handler verifies: `payment.Appointment.ArtistId == myArtistId` when caller is `Artist`.
- When caller is `Owner`: can confirm any appointment's cash.
- On confirm: sets `Payment.Status = Paid`, `Appointment.DepositStatus = Paid`.
  Both must be updated atomically (same `SaveChangesAsync` call).

#### A7. Notification scope

File: `GetNotificationsHandler.cs`, `UpdateNotificationPreferencesHandler.cs`

Verify:
- `GetNotifications` returns only notifications for `UserId == currentUserId`.
  NOT all notifications for the tenant.
- `UpdateNotificationPreferences` saves preferences for `UserId == currentUserId`.
  If preferences don't exist yet for this user, creates them.

---

### Layer B — Frontend State (Artist Perspective)

#### B1. artistsApi.ts — GetMyArtist error handling

`ArtistLayout` calls `useGetMyArtistQuery()`. The `myArtist?.id` is used dynamically
in the nav link (`/artists/${myArtist.id}`).

Verify:
- If `getMyArtist` returns an error (e.g., this user has no artist record), the layout
  doesn't crash. The "My Portfolio" nav item simply doesn't render (`myArtist && ...`).
- The `ArtistResponse.portfolioImages` field is `string[]`. If the backend returns
  `portfolioImages: null` (instead of `[]`) for a new artist, the component that reads
  `.portfolioImages.map(...)` will crash. Add a `?? []` fallback.

#### B2. ArtistDetailPage — "Schedule" tab queries ALL appointments

In `ArtistDetailPage`, the Schedule tab calls:
```ts
const { data: allAppointments = [] } = useGetAppointmentsQuery({}, { skip: !id });
const artistAppointments = allAppointments.filter((a) => a.artistId === id);
```

This loads ALL studio appointments (potentially hundreds) and filters client-side.
This is:
1. A performance issue.
2. A data-visibility issue (if artist visits another artist's detail page, they'd
   see all appointments, then filter — the data is still transferred).

Fix: pass `artistId` as a query param if the caller is an owner:
```ts
const { data: appointments = [] } = useGetAppointmentsQuery(
  canManage ? { artistId: id } : {},
  { skip: !id }
);
```
For an artist viewing their own profile, the backend `GetAppointmentsQuery` already
filters by `myArtistId` — no need to filter client-side. Only owners need the `artistId`
param to view a specific artist's schedule.

After this fix, `artistAppointments` should be renamed to just `appointments` since
they're already filtered.

#### B3. useGetDesignsQuery — artist filter

`ArtistDetailPage` tab "Designs" calls:
```ts
const { data: designs = [] } = useGetDesignsQuery({ artistId: id! }, { skip: !id });
```

This passes `artistId` to filter designs by that artist. Verify:
- The `designsApi` endpoint sends `params: { artistId }` to the backend.
- The backend `GetDesignsQuery` handler applies this filter ONLY for owners. If an
  artist passes `artistId` of another artist, the backend should either ignore it
  (returning only their own designs) or reject it with 403.

#### B4. SuspensionBanner — artist role prop

`ArtistLayout` renders `<SuspensionBanner role="artist" />`.
Read `SuspensionBanner.tsx` and verify it accepts the `role` prop.

From the owner prompt: `<SuspensionBanner studio={studio} />` takes a `studio` prop.
The artist version takes `role="artist"`. These are different call signatures.

Verify `SuspensionBanner.tsx` handles both shapes:
- If `studio` prop: shows banner when `!studio.isActive`.
- If `role="artist"`: should still show the suspension banner, but the artist has no
  direct access to `studio.isActive`. Fix: the `SuspensionBanner` for artist role
  should call `useGetMyStudioQuery()` (if the artist API has a read endpoint for studio
  metadata), OR read subscription state. The simplest fix: use the `ReadOnlyBanner`
  which already reads subscription status — suspension is communicated via subscription
  being `"Suspended"` status.

If `SuspensionBanner` has a different implementation for artists vs owners, verify both
branches work correctly and don't crash.

#### B5. ReadOnlyBanner — skip for non-owners

`ReadOnlyBanner` calls `useGetSubscriptionQuery()`. An artist user has no subscription
(that's an owner concept). Verify:
- `ReadOnlyBanner` either skips the query when the role is not `Owner`, OR
- The `/api/v1/billing/subscription` endpoint returns a sensible response for artist
  role (even though artists don't own subscriptions, the studio's subscription
  affects their access).
- The `ReadOnlyBanner` does NOT show an error state when the artist user gets a 403
  from the subscription endpoint.

Recommended fix if the endpoint 403s for artists: add `skip: role !== Role.Owner`
to the query inside `ReadOnlyBanner`.

---

### Layer C — Frontend Components (Artist Perspective)

#### C1. ArtistLayout

Verify:
- 7 static nav items + dynamic "My Portfolio" link render correctly.
- "My Portfolio" link uses `myArtist.id` — verify this never renders with `undefined`
  in the URL (would create `/artists/undefined`).
- `useSignalR(tenantId)` is called — real-time notifications work for artists.
- Mobile nav: 8 nav items (7 static + 1 dynamic) will overflow on narrow screens.
  Add `overflow-x-auto scrollbar-none shrink min-w-0` to the `<nav>` element.
- No subscription priming in ArtistLayout (correct — artist shouldn't call billing).
  Verify `useGetSubscriptionQuery` is NOT called here (it IS called in `OwnerLayout`
  to prime the cache for `SubscriptionGatedButton` — but in artist layout, that cache
  won't be warm, so `SubscriptionGatedButton` must handle the undefined subscription case).

#### C2. SchedulePage (artist view)

The schedule page is shared between owner and artist, but the data must be filtered:
- **Owner sees**: all studio appointments.
- **Artist sees**: only their own appointments.

Verify the `GetAppointmentsQuery` correctly filters by artist when called by an artist.
If not, the schedule page shows other artists' appointments — a privacy issue.

Also verify:
- Empty week state shows correctly when the artist has no appointments.
- Day grouping only shows days that have appointments (days without are skipped — ✓ from
  the code, but verify no empty-day rows appear).
- Today's date highlighted in the day header. ✓ (already uses `text-primary`).
- Week navigation buttons work correctly.
- "Today" button returns to current week. ✓

**Missing feature to note for Phase 2:** The artist cannot book a new appointment from
`SchedulePage`. The "Book Appointment" CTA should be accessible from this view for
artists (they can create appointments for their clients). Track this for Phase 2.

#### C3. AppointmentDetailPage (artist view)

The artist can: confirm, complete, mark no-show, cancel their own appointments.
The artist CANNOT: reschedule (owner only — verify this).

Verify:
- `isArtistPlus` (from `usePermission(Role.Artist)`) is `true` for artist role.
- `canOwner` (from `usePermission(Role.Owner)`) is `false` for artist role.
- Buttons shown for `Pending` status: "Confirm" + "Cancel" (same as owner ✓).
- Buttons shown for `Confirmed` status: "Complete" + "Mark No-Show" + "Cancel".
  "Reschedule" should ONLY show when `canOwner` is true.
- Terminal statuses (Completed, Cancelled, NoShow): no action buttons. ✓
- Cash deposit: `CashDepositConfirmButton` shows when `payment.status === CashPending`.
  Artist can confirm their own client's cash. ✓
- "Add to calendar" / ICS download: verify this works for artist (they need to add
  their appointment to their personal calendar).

#### C4. ArtistListPage (artist viewing the list)

Artists can VIEW the list but cannot CREATE or DELETE other artists.

Verify:
- `canManage` is `false` for artist role → "Add Artist" button is hidden. ✓
- Delete buttons per row are hidden for artist. ✓
- Search + specialization filter still works. ✓
- Each row is clickable → navigates to `/artists/:id`. ✓
- The artist's own row shows something to indicate "this is you" — or at minimum,
  clicking navigates to their own profile where they can edit it.

**Potential bug:** `ArtistListPage` uses `useEffect` to reset `selectedSpec` when
`search` changes. This is a state-management side-effect, not data fetching, so it's
technically allowed BUT it's a pattern to watch. Verify this `useEffect` doesn't cause
infinite re-renders.

#### C5. ArtistDetailPage (artist viewing their OWN profile)

This is the artist's primary self-service screen ("My Portfolio").

Verify:
- `isOwnProfile = isArtistRole && artist.userId === currentUserId` is computed correctly.
- `canManagePortfolio = canManage || isOwnProfile` allows the artist to manage their
  OWN portfolio even though they can't manage others'. ✓
- Edit button shows for `isOwnProfile` even when `!canManage`. Verify this — currently
  the edit button shows only when `canManage`:
  ```tsx
  {canManage && !isEditing && (<div>Edit + Delete buttons</div>)}
  ```
  **Bug:** Artist cannot edit their own profile because `canManage` is false for artists.
  The edit button should show for `isOwnProfile` too. Fix:
  ```tsx
  {(canManage || isOwnProfile) && !isEditing && (
    <div className="flex items-center gap-2">
      <Button onClick={startEdit}>Edit</Button>
      {canManage && <Button onClick={() => setDeleteOpen(true)}>Delete</Button>}
    </div>
  )}
  ```
  The Delete button remains owner-only.

- On save: `updateArtist` is called. The backend MUST verify the artist is editing
  their own profile (see A1). If the backend doesn't enforce this, an artist could edit
  another artist's profile by changing the URL. Fix both layers.

- Portfolio tab:
  - "Add image" button: visible when `canManagePortfolio`. ✓
  - Remove image button: visible when `canManagePortfolio`. ✓
  - `openImagePicker` function uses `document.createElement("input")` to trigger
    a file picker — this is a browser DOM side-effect in an event handler, which is
    acceptable. Verify it works (no stale closure issue with `artist.portfolioImages`).

- Schedule tab:
  - Currently shows past/upcoming appointments (from `GetAppointmentsQuery`).
  - **Missing:** The artist should also be able to VIEW and EDIT their weekly working
    hours from their own profile. Add a "My Schedule" sub-section that shows and
    edits their availability. (Backend: `GetArtistScheduleQuery` + `UpsertArtistScheduleCommand`).
    This is a critical missing feature — artists need to set their availability.
    Track for Phase 2 but note the frontend entirely lacks a schedule-editing UI
    for artists on their profile.

- Designs tab:
  - Shows designs assigned to this artist. ✓
  - Links to `/designs/:id`. ✓

#### C6. DesignListPage (artist view)

Verify:
- Shows only this artist's designs (backend-filtered).
- Status filter tabs: All | Draft | InReview | ChangesRequested | Approved.
- "New Design" button → `/designs/new`. ✓ (artist can create designs)
- Status badge colours are correct.
- Empty state and no-match state both present.
- Loading skeleton visible during initial load.
- "InReview" items are the priority — verify they're sorted to show first or are
  visually distinct (client is waiting for action).

#### C7. DesignDetailPage (artist view)

Verify:
- Title, client name, status badge render. ✓
- Revision history shows all revisions with upload dates.
- Latest revision image renders. If R2 URL has expired, shows a broken image fallback.
  Verify the `onError` handler on the `<img>` hides the broken image and shows a
  placeholder (same pattern as portfolio images in `ArtistDetailPage`).
- Artist actions:
  - `Draft` or `ChangesRequested` → "Upload Revision" button → `/designs/:id/upload`.
  - `InReview` → "Waiting for client approval" message. No action buttons for artist.
    **Bug to check:** Does the artist see "Approve" and "Request Changes" buttons
    (which should be client-only)? The `canReview` prop on `RevisionCard` controls this.
    Verify `canReview` is `false` for artists.
  - `Approved` → "Design approved" badge. No further actions.
- Share token:
  - `ShareDesignButton` shows for `ArtistAndAbove`. ✓
  - "Generate share link" → creates token. Link copies to clipboard (copy button). ✓
  - "Revoke" button → confirm step. ✓

#### C8. UploadRevisionPage

This is an artist's primary daily action — uploading a new design revision for client review.

Verify:
- `FileUploadField` accepts image files only.
- Shows upload progress indicator.
- "Upload" button is disabled until a file is selected.
- On success: navigates to `/designs/:id` and shows the new revision. ✓
- On error: shows a descriptive error message (not just "Something went wrong").
  Common errors: file too large (over R2 limit), file type rejected, network timeout.
- Loading states: button shows spinner during upload.
- After navigating back to `DesignDetailPage`, the new revision appears immediately
  (RTK Query invalidation tag `["Design", { id }]` handles this). Verify the tag
  is set on `uploadRevision` mutation in `designsApi`.

#### C9. ClientListPage (artist view)

Artists see the same client list as owners. Verify:
- Shows client name, email, "View" link.
- "Add Client" button present (artist can add clients). ✓
- Search by name or email. ✓
- Empty state + no-match state.

**Potential bug:** The client list might include clients who have never had an
appointment with THIS artist — the artist can see all studio clients. This is by
design (studio-level, not artist-level) but confirm it's intentional and document it.

#### C10. IntakeFormListPage + ConsentFormListPage

Artists read these forms but never fill them (clients do). Verify:
- Both list pages are read-only for artists.
- No "Create form" or "Sign form" buttons visible to artists.
- Each item links to the detail page for reading.
- Detail pages are purely read-only.
- Empty states are present.

#### C11. DepositRuleListPage (artist view)

Artists read deposit rules to know how much to collect, but cannot create/edit/delete them.

Verify:
- "Create rule" button is hidden for artists (`canManage` guard). ✓
- Each rule shows: name, type, value.
- No edit or delete buttons per row.
- Links to `DepositRuleDetailPage` (read-only view).
- If `DepositRuleDetailPage` has an edit form, verify it's hidden for artists.

#### C12. NotificationLogListPage

Verify:
- Shows only notifications for the currently logged-in artist user.
- "Mark all read" bulk action works.
- Empty state.
- `NotificationBell` in the header updates the unread count after "Mark all read".

#### C13. NotificationBell

Verify:
- Unread count badge shows correctly.
- Dropdown opens on click and closes on outside-click (useEffect approved for this). ✓
- "Mark all read" in dropdown calls the mutation. ✓
- Individual notifications link to the relevant page (appointment detail, design detail, etc.).
- Notifications for the artist include:
  - New appointment created (for them)
  - Appointment confirmed by owner
  - Design approved by client
  - Design: client requested changes
  - Intake form submitted (for a client linked to their appointments)
  - Cash deposit received (confirmation needed)

---

### Layer D — Test Suite Completeness

#### D1. ArtistLayout.test.tsx

Required tests:
- Renders 7 static nav items
- "My Portfolio" link renders when `getMyArtist` succeeds
- "My Portfolio" link is hidden when `getMyArtist` returns null/error
- `useSignalR` is called with tenantId
- Logout dispatches logout action and navigates to /login
- `SuspensionBanner` renders in the layout
- `ReadOnlyBanner` renders in the layout
- Mobile nav has overflow-x-auto class

#### D2. SchedulePage.test.tsx (artist context)

Required tests:
- Artist sees only their own appointments
- Does NOT see appointments belonging to other artists
- Empty week state shows when no appointments
- Today is highlighted in day headers
- Week navigation changes date range
- Today button returns to current week
- Appointment card navigates to `/appointments/:id`

#### D3. AppointmentDetailPage.test.tsx (artist context)

Required tests:
- Artist sees Confirm and Cancel for Pending appointments
- Artist sees Complete, No-Show, Cancel for Confirmed appointments
- Reschedule button is hidden for artist role
- Confirm calls mutation, shows toast
- Cancel shows dialog, calls mutation on confirm
- Complete shows confirmation dialog
- No actions shown for terminal statuses
- CashDepositConfirmButton shows for CashPending payment
- "Add to calendar" link present

#### D4. ArtistDetailPage.test.tsx (artist own profile)

Required tests:
- Edit button shows when artist is viewing own profile
- Delete button is hidden for artist (only owner)
- Portfolio tab "Add image" button shows for own profile
- Portfolio tab "Remove" button shows for own profile
- Saving edit form calls updateArtist mutation
- Portfolio image remove calls updateArtistPortfolio mutation
- Schedule tab shows own appointments
- Designs tab shows own designs
- Delete dialog: shown only for owner role

#### D5. DesignDetailPage.test.tsx (artist context)

Required tests:
- Approve and Request Changes buttons are hidden for artist role
- Upload Revision link shows when status is Draft or ChangesRequested
- "Waiting for client" message shows when status is InReview
- Share token: Generate button calls mutation
- Share token: Revoke button shows confirm step
- Broken image shows fallback placeholder
- Revision history shows upload dates

#### D6. UploadRevisionPage.test.tsx

Required tests:
- Upload button disabled until file selected
- Shows spinner during upload
- On success: navigates to design detail page
- On error: shows descriptive error message
- Accepted file types are image types only

#### D7. ArtistListPage.test.tsx (artist context)

Required tests:
- Add Artist button hidden for artist role
- Delete button per row hidden for artist role
- Search filters list
- Specialization filter chips filter list
- Row click navigates to artist detail
- "This is you" indicator shows on own row (if implemented)

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

Evaluate every artist-facing screen as a product manager who has spent a week in a
real tattoo studio. The artist uses this app between sessions, during client consultations,
and at the front desk. Every extra click is time away from a client.

---

## P1. Navigation & Layout

### P1.1 Document titles
Every artist-accessible page needs a descriptive browser tab title.
Create `useDocumentMeta` in `shared/utils/useDocumentMeta.ts` if not already present:
```ts
import { useEffect } from "react";
export function useDocumentMeta(title: string) {
  useEffect(() => { document.title = title; }, [title]);
}
```
Required titles:
- Schedule:             "My Schedule — Pena e Artë"
- Clients:              "Clients — Pena e Artë"
- Designs:              "Designs — Pena e Artë"
- Design detail:        "{title} — Designs — Pena e Artë"
- Intake Forms:         "Intake Forms — Pena e Artë"
- Consent Forms:        "Consent Forms — Pena e Artë"
- Deposit Rules:        "Deposit Rules — Pena e Artë"
- Notifications:        "Notifications — Pena e Artë"
- My Portfolio:         "{firstName} {lastName} — My Portfolio — Pena e Artë"
- Appointment detail:   "Appointment — Pena e Artë"

### P1.2 Mobile nav overflow
ArtistLayout has up to 8 nav items. Add:
```tsx
<nav className="ml-6 flex items-center gap-1 overflow-x-auto scrollbar-none shrink min-w-0">
```

### P1.3 Per-route error boundaries
Wrap artist routes in `ErrorBoundary` inside `router.tsx`, same as issuer routes:
```tsx
{ index: true, element: <ErrorBoundary><SchedulePage /></ErrorBoundary> },
```
Do this for every artist route.

### P1.4 "My Portfolio" nav item robustness
The dynamic nav link depends on `useGetMyArtistQuery()`. Add a loading state:
```tsx
{myArtistLoading ? (
  <span className="px-3 py-1.5 rounded-md text-sm text-muted-foreground/50 animate-pulse">
    Portfolio
  </span>
) : myArtist ? (
  <NavLink to={`/artists/${myArtist.id}`}>…My Portfolio…</NavLink>
) : null}
```

---

## P2. Schedule Page Polish

### P2.1 Appointment creation from schedule

The most critical missing feature: the artist needs to be able to create a new
appointment directly from the schedule page.

Add a "+ New Appointment" button in the `SchedulePage` header. Clicking it opens an
inline form or navigates to an appointment creation flow. The form needs:
- Client selector (from `useGetClientsQuery`)
- Date and time pickers
- Duration (minutes, default 60)
- Notes (optional)
- The artist's own ID pre-filled (not selectable by the artist themselves — they can
  only create appointments for themselves)

If `BookAppointmentForm` exists as a shared component: use it. If it's only accessible
from `/book` (client route): extract the form logic into a shared component that both
the client booking flow and the artist creation flow can use.

### P2.2 Appointment card — show client name

Currently `AppointmentCard` shows: date + duration + status badge.
Add the **client name** to the card — this is the primary information an artist needs
at a glance. Source: `appt.clientName` (verify this field is in `AppointmentResponse`).
If `clientName` is not in the response, add it to `GetAppointmentsHandler` projection.

### P2.3 Colour coding by status

In `SchedulePage`, colour-code the appointment rows by status:
- Confirmed → subtle green-left-border
- Pending → subtle amber-left-border
- Completed → muted/faded
- Cancelled / NoShow → struck-through or muted

```tsx
const statusBorder: Record<AppointmentStatus, string> = {
  Confirmed:  "border-l-4 border-l-green-500/60",
  Pending:    "border-l-4 border-l-amber-500/60",
  Completed:  "border-l-4 border-l-muted/40 opacity-60",
  Cancelled:  "border-l-4 border-l-destructive/30 opacity-50",
  NoShow:     "border-l-4 border-l-destructive/30 opacity-50",
};
```

### P2.4 "Next up" indicator

In `SchedulePage`, for today's day section, highlight the NEXT upcoming appointment
(first one with `date > now` and non-terminal status) with a "Next →" badge or a
subtle ring:
```tsx
const isNext = dayAppt === nextAppointment;
```

---

## P3. Artist Profile ("My Portfolio") Polish

### P3.1 Working hours / schedule editing

The artist must be able to set their working hours from their own profile.
This is currently entirely missing from `ArtistDetailPage`.

Add a new section below the appointment list in the "Schedule" tab (or a new
"Availability" tab):

```
My Working Hours
Mon  09:00 – 18:00  [Edit]
Tue  09:00 – 18:00  [Edit]
...
Sat  10:00 – 15:00  [Edit]
Sun  Closed
```

Implementation:
1. Call `useGetArtistScheduleQuery(id)` to load the existing schedule.
2. Inline edit: clicking "Edit" on a day opens an input row with start/end time pickers.
3. Saving calls `useUpsertArtistScheduleMutation()`.
4. "Add time off" button opens a date-range picker. Saves with `useAddArtistTimeOffMutation()`.
5. Time-off entries listed below the weekly schedule with delete buttons.

Add these mutations to `artistsApi.ts` if they're missing:
```ts
getArtistSchedule:      builder.query<ArtistScheduleResponse, string>({…})
upsertArtistSchedule:   builder.mutation<void, { id: string; days: ScheduleDay[] }>({…})
addArtistTimeOff:       builder.mutation<void, { id: string; start: string; end: string }>({…})
deleteArtistTimeOff:    builder.mutation<void, { artistId: string; timeOffId: string }>({…})
```

### P3.2 Bio field on artist profile

The public artist portfolio page (`/artist/{slug}`) shows an artist bio. The artist
needs to be able to set this from their own profile.

Add `bio?: string` to `UpdateArtistRequest` and to the edit form in `ArtistDetailPage`.
Show a `<Textarea>` for bio in the edit form. Max 1000 characters.
Verify the backend `UpdateArtistCommand` persists `bio` to the `Artist.Bio` column.

### P3.3 Profile image upload

The public artist page shows an avatar. Artists need to upload their own profile photo.

In the `ArtistDetailPage` header (avatar section): when `isOwnProfile`, add an
"Upload photo" overlay on the avatar:

```tsx
<div className="relative h-14 w-14 group">
  <Avatar className="h-14 w-14">
    {artist.avatarUrl
      ? <AvatarImage src={artist.avatarUrl} alt={`${artist.firstName} photo`} />
      : <AvatarFallback>{getInitials(…)}</AvatarFallback>
    }
  </Avatar>
  {isOwnProfile && (
    <button
      onClick={handleAvatarUpload}
      className="absolute inset-0 rounded-full bg-black/50 opacity-0 group-hover:opacity-100
                 transition-opacity flex items-center justify-center focus-visible:opacity-100"
      aria-label="Change profile photo"
    >
      <Camera className="h-5 w-5 text-white" />
    </button>
  )}
</div>
```

`handleAvatarUpload` uses the same `openImagePicker` + `upload` pattern but calls
`updateArtist({ id, body: { …existingValues, avatarUrl: publicUrl } })`.

Add `avatarUrl` to `UpdateArtistRequest` in both the TS type and the backend command.

### P3.4 Instagram handle on artist profile

Artists have an `instagramHandle` field used on the public portfolio page.
The artist should be able to set this from their own profile.

Add `instagramHandle?: string` to the edit form. Strip the leading `@` on save.
Show below the email field in the profile view:
```
@ instagram_handle
```

### P3.5 Public portfolio link

When the artist has a `slug`, show a link to their public page from the "My Portfolio"
screen:
```tsx
{artist.slug && (
  <a
    href={`${import.meta.env.VITE_PUBLIC_URL}/artist/${artist.slug}`}
    target="_blank"
    rel="noopener noreferrer"
    className="text-xs text-muted-foreground flex items-center gap-1 hover:text-foreground"
  >
    View public profile
    <ExternalLink className="h-3 w-3" />
  </a>
)}
```

---

## P4. Design Workflow Polish

### P4.1 Design list: InReview items flagged urgently

When a design is `InReview`, the client is waiting for the artist to see if feedback
came back. But actually — the artist is waiting for the CLIENT to approve. So "InReview"
items are not urgent for the artist.

What IS urgent for the artist: `ChangesRequested` — the client wants changes and the
artist needs to upload a new revision.

Update the `DesignListPage` sort: show `ChangesRequested` designs first.

Add a banner on `DesignDetailPage` for `ChangesRequested`:
```tsx
{design.status === "ChangesRequested" && (
  <div className="rounded-md border border-amber-500/30 bg-amber-500/10 px-3 py-2.5
                  flex items-center gap-2 text-sm text-amber-700 dark:text-amber-400">
    <AlertTriangle className="h-4 w-4 shrink-0" />
    <span>The client has requested changes. Upload a new revision to continue.</span>
  </div>
)}
```

### P4.2 Share link QR code

When a share token is generated, show a small QR code alongside the copy-link button.
The QR points to the share URL (`{VITE_PUBLIC_URL}/share/{token}`).

Use `QRCoder` (already a pre-approved NuGet package). Add a new endpoint:
```
GET /api/v1/designs/{id}/share-token/qr?format=png
Returns: image/png
AllowAnonymous: No — ArtistAndAbove (the token itself is public, but this endpoint
generates the QR image which requires the share token to exist)
```

Frontend: show the QR image inline in `ShareDesignButton` alongside the copy button.

### P4.3 Revision notes from client

When the client requests changes, they can include `notes`. These notes are currently
in `RevisionCard` but may not be visually prominent.

Ensure: when `revision.approvalStatus === "ChangesRequested"` and `revision.approvalNotes`
is non-empty, show the notes in an amber callout box:
```tsx
{revision.approvalNotes && revision.approvalStatus !== "Approved" && (
  <blockquote className="mt-2 pl-3 border-l-2 border-amber-500 text-sm text-muted-foreground italic">
    "{revision.approvalNotes}"
  </blockquote>
)}
```

---

## P5. Client Interaction Polish

### P5.1 Quick client look-up from appointment

When the artist is viewing an `AppointmentDetailPage`, they often want to look at the
client's profile. Add a "View client" link in the appointment detail:
```tsx
<Link to={`/clients/${appt.clientId}`} className="text-sm text-violet-500 hover:underline">
  View {appt.clientName}'s profile →
</Link>
```
Verify `AppointmentResponse` includes `clientId`.

### P5.2 Client's intake form from appointment

If the client has submitted an intake form, show a link from `AppointmentDetailPage`:
"View intake form →". Source: query `IntakeFormsQuery` filtered by `clientId`, check if
any exist.

Alternatively, show a compact "Client info" section on the appointment detail:
allergies + medical notes from `GetClientProfileQuery({ clientId: appt.clientId })`.
This saves the artist from navigating to the client detail page before each appointment.

### P5.3 Tattoo history context on client detail

When the artist is viewing a client's profile, show the client's tattoo history in a
compact gallery format (masonry or grid). Each tattoo shows:
- Placement label
- Style
- Thumbnail image (if available)
- Date

This gives the artist context about the client's existing tattoos before planning a
new piece.

---

## P6. Notifications Polish

### P6.1 Notification links to correct context

Each notification type should link directly to the relevant entity:
- `AppointmentCreated` → `/appointments/{id}`
- `DesignApproved` → `/designs/{id}`
- `DesignChangeRequested` → `/designs/{id}`
- `IntakeFormSubmitted` → `/forms/intake/{id}`
- `ConsentFormSigned` → `/forms/consent/{id}`
- `CashDepositDeclared` → `/appointments/{id}`

Verify `NotificationLogListPage` renders these as `<Link>` components.

### P6.2 Notification preferences for artist

`NotificationPreferencesCard` should be accessible from the artist's profile or settings.
Currently it's in `StudioProfilePage` (owner-only). Add it as a section in
`ArtistDetailPage` under the "Profile" tab when `isOwnProfile`:
```tsx
{isOwnProfile && <NotificationPreferencesCard />}
```

---

## P7. Global Polish Items (Artist-Specific)

### P7.1 Toast notifications for all artist mutations

Every artist mutation should fire a Sonner toast. Verify these are present:
```
Update own profile:         "Profile updated"
Upload portfolio image:     "Image added to portfolio"
Remove portfolio image:     "Image removed"
Upsert artist schedule:     "Schedule updated"
Add time off:               "Time off added"
Delete time off:            "Time off removed"
Confirm appointment:        "Appointment confirmed"
Cancel appointment:         "Appointment cancelled"
Complete appointment:       "Session complete"
Mark no-show:               "Marked as no-show"
Upload design revision:     "Revision uploaded"
Revoke share token:         "Share link revoked"
Confirm cash payment:       "Cash payment of €X confirmed"
Create client:              "Client added"
Add tattoo record:          "Tattoo record saved"
Delete tattoo record:       "Record deleted"

Error:                      error.data?.message ?? "Action failed. Try again."
```

### P7.2 All destructive actions need confirmation

Artist-accessible destructive actions that must have an inline confirm step:
- Remove portfolio image (currently removes immediately — add confirm)
- Delete tattoo record
- Cancel appointment (already has a Dialog ✓)
- Mark no-show (significant — adds a "no-show" to client's record)
- Delete design revision
- Revoke share token

For portfolio image removal: since it's a grid button, use a brief inline confirm:
```tsx
// On first click: set pendingRemoveUrl to the url
// Show a small "Remove?" row at the bottom of that image
// On confirm: call removePortfolioImage
// On cancel: reset pendingRemoveUrl
```

### P7.3 Spinner + disabled on all mutation buttons

Every mutation button used by artists must show a spinner while in-flight and be
`disabled` during that time. Audit all action buttons in artist-accessible components.

### P7.4 Error states with retry on every query

Every RTK Query result used in artist components must have an `isError` branch
that renders:
```tsx
<p role="alert" className="text-sm text-destructive py-4">
  Failed to load {dataType}.{" "}
  <button className="underline" onClick={refetch}>Try again</button>
</p>
```
Check: `SchedulePage`, `ArtistDetailPage`, `DesignListPage`, `DesignDetailPage`,
`ClientListPage`, `ClientDetailPage`, `IntakeFormListPage`, `ConsentFormListPage`.

### P7.5 Accessible portfolio image grid

The portfolio grid in `ArtistDetailPage` uses `<img alt="Portfolio image">` — all images
have the same `alt`. Change to descriptive alt text:
- If the image has style metadata: `alt={`Portfolio tattoo — ${style}`}`
- Otherwise: `alt={`Portfolio tattoo ${index + 1} by ${artist.firstName}`}`

Also verify the remove button has a unique `aria-label`:
```tsx
aria-label={`Remove portfolio image ${index + 1}`}
```

---

## Phase 2 Exit Condition

After all polish items:

1. `pnpm test` — all green.
2. `dotnet test` — all green.
3. `pnpm build` — no TypeScript errors.
4. `dotnet build` — no warnings.
5. Self-review — walk through every artist page and answer:
   - Document title set?
   - Loading, error, and empty states on every list?
   - Validation with per-field inline errors on every form?
   - Toast on every mutation success and error?
   - Confirmation on every destructive action?
   - Spinner on every in-flight mutation button?
   - Back link on every detail page?
   - Retry button on every query error?
   - All list pages have an empty state?
   - Artist can ONLY modify their OWN data (not another artist's)?

---

## Final Deliverable

When both phases exit cleanly, add an entry to `docs/claude/architecture.md`
under `## Artist QA Pass — 2026-07-01` listing:

1. Every bug found and fixed (file → bug → fix).
2. Every polish item implemented.
3. Any architecture decisions made → add to the Decisions Log table.
4. Any items deferred or skipped, with reason.
