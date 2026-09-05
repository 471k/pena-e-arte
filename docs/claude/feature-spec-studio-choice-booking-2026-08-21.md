# Feature Spec — Studio-Choice Booking

> Date: 2026-08-21
> Status: Draft for review — **not** an overnight master prompt yet. Section 9
> ("Open questions") must be resolved before this becomes implementation-ready.
> Touches: `Pena_e_Arte.Domain`, `.Contracts`, `.Application` (Appointments), `.Infrastructure`
> (one EF migration), `.API`, `frontend/src/features/appointments`, Help Menu
> (`helpContent.ts`), standalone user manual (`index.html`), possibly `clientTour.ts`.

---

## 1. What this is

Today a client booking an appointment must pick one specific artist — `Appointment.ArtistId`
is a required `Guid`, and every part of the booking pipeline (schedule check, conflict check,
deposit calculation, slot locking) is keyed on that one artist. There is no way to book "with
the studio" and let the owner pick who does the work.

This spec adds a second booking path, alongside the existing one:

1. **Book a specific artist** — unchanged, exactly as it works today.
2. **Book with the studio** — the client picks a date/time and duration only; the system
   confirms at least one active artist could plausibly do it, and the appointment is created
   with no artist attached. An Owner must assign an artist before the appointment can move
   from Pending to Confirmed.

This is a different feature from the `Client.ArtistId` "preferred artist" field shipped
2026-08-20 (`overnight-prompt-client-artist-assignment-2026-08-20.md`) — that's a persistent
field on the client record; this is a per-booking choice on the appointment.

---

## 2. Current state — verified against live source, 2026-08-21

Read directly, not inferred from `architecture.md` (its Feature Module Map has no entry for
this because it doesn't exist yet):

- `Pena_e_Arte.Domain/Entities/Appointment.cs` — `ArtistId` is a non-nullable `Guid`, `Artist`
  is a non-nullable navigation property.
- `Pena_e_Arte.Contracts/Requests/CreateAppointmentRequest.cs` and
  `Application/Appointments/Validators/CreateAppointmentValidator.cs` — `ArtistId` is
  `NotEmpty()`-validated, required.
- `Application/Appointments/Commands/CreateAppointmentCommand.cs` — loads one `Artist`,
  checks that artist's `ArtistSchedule`, `ArtistTimeOff`, and conflicting `Appointment` rows,
  acquires an `ISlotLocker` lock keyed `(studioId, artistId, date)`, computes
  `DepositAmount` via `DepositCalculator.Calculate(rule, artist.HourlyRate, duration)`.
- `Application/Appointments/Queries/CheckSlotAvailabilityQuery.cs` — same single-artist
  schedule/time-off/conflict check, used by the frontend's pre-submit availability probe.
- `Application/Appointments/Commands/RescheduleAppointmentCommand.cs` — conflict check is
  `a.ArtistId == appointment.ArtistId`.
- `Application/Appointments/Commands/ConfirmAppointmentCommand.cs` — only checks
  `Status == Pending`; no artist-ownership check exists at all today (see §8, flagged).
- `Application/Appointments/Queries/GetAppointmentsQuery.cs` — Artist-role callers are scoped
  to `a.ArtistId == myArtistId`; Owner/Issuer see everything, unfiltered by default.
- `Application/Appointments/Queries/GetAppointmentQuery.cs` (singular) — **no role scoping at
  all**, relies only on the endpoint's `ArtistAndAbove` policy + tenant query filter.
- `Contracts/Responses/AppointmentResponse.cs` — has `ArtistId` but **no `ArtistName`** —
  the artist is never denormalized into this response, unlike `ClientName`.
- `frontend/.../AppointmentDetailPage.tsx` — does **not render an Artist row at all** today.
  The "Confirm appointment" button is shown to any `Role.Artist`-and-above caller whenever
  `status === Pending`, with no ownership check. `ReminderDialog` is passed
  `artistId={appt.artistId}` unconditionally (currently safe because it's never null).
- `frontend/.../BookAppointmentForm.tsx` — `artistId` is a required zod field; step 2 (deposit
  payment) only renders `if (booked.depositAmount > 0)` — **it already no-ops cleanly when
  depositAmount is 0**, which matters for §6.
- `frontend/.../MyBookingsSection.tsx`'s `DepositArea` — reactively reads
  `appt.depositAmount`/`appt.depositStatus` on every render; it will start showing "Pay
  deposit" the moment those fields change server-side, with **no frontend code change needed**
  for that to work.
- `Domain/Services/DepositCalculator.cs` — a percent-based rule with `artistHourlyRate: null`
  already returns `0m` (no crash, no special-casing needed); a fixed-amount rule is
  unaffected by artist rate either way.
- `Domain/Constants/AuditActions.cs` — `ClientArtistReassigned` is the direct precedent for
  the new audit action this spec adds.
- `docs/claude/database.md` — nullable-column migrations are the established zero-downtime
  pattern; this spec's migration loosens an *existing* required column instead of adding one
  (see §3).

---

## 3. Decisions

Locked this session (product owner, via clarifying questions on 2026-08-21):

| # | Decision | Rationale |
|---|---|---|
| 1 | Studio-choice availability check is **any-artist**: the system checks that at least one active artist has open schedule, no time-off, and no conflicting appointment at the requested date/time — same rigor as today's single-artist check, run across all active artists instead of one. | Confirmed by product owner. Coarser options (no check at all, or studio-level open-hours only) were considered and rejected. |
| 2 | Artist assignment is **required before Confirm**. A studio-choice appointment sitting at `Status == Pending && ArtistId == null` cannot be confirmed until an Owner assigns an artist. | Confirmed by product owner. Mirrors the existing Pending→Confirmed owner action; no new appointment status needed (see #3). |
| 3 | **No new `AppointmentStatus` enum value.** "Needs artist" is a computed state (`Pending && ArtistId == null`), not a stored one. | Proposed, not yet confirmed. Minimal invasive change; avoids touching every status-based switch across frontend/backend for a state that's really "Pending, missing one field." Flagged in §9 for explicit sign-off since it's a design call, not something the user was asked directly. |
| 4 | New endpoint `PATCH /api/v1/appointments/{id}/artist`, `OwnerOnly`. Accepts a **required** `ArtistId` (assignment only moves forward — no unassign-back-to-null). Re-validates that specific artist's schedule/time-off/conflict at the appointment's exact date/time, independent of the softer any-artist check done at booking time. | Mirrors `UpdateClientArtistCommand`'s `OwnerOnly` precedent exactly (roster/staff-assignment action). Re-validation is necessary because "some artist was free" at booking time doesn't guarantee *this* artist is. |
| 5 | `AssignAppointmentArtistCommand` recomputes `DepositAmount` via the existing `DepositCalculator` when the current amount is `0` and `DepositStatus == Pending` (i.e. a percent-based rule had no rate to work from at booking time). Fixed-amount-rule bookings are already correct and untouched. | Makes the existing "Charge deposit" button and `MyBookingsSection`'s reactive `DepositArea` pick up the real amount automatically, with zero other frontend changes (see §2). Flagged in §9 as the single biggest open product question — it's a payment-timing change, not just a UI addition. |
| 6 | `CreateAppointmentCommand`, for a studio-choice booking, does **not** acquire an `ISlotLocker` lock. | There is no specific artist resource being claimed yet — the real single-resource claim happens at assignment time, and `AssignAppointmentArtistCommand` should acquire the lock then (see §4.3). |
| 7 | `AppointmentResponse` gains `ArtistName` (nullable), denormalized exactly like `ClientResponse.ArtistName` from yesterday's client-artist-assignment prompt. | Currently absent entirely — needed to render "Unassigned" / the artist's name on `AppointmentDetailPage.tsx`, which has no Artist row today at all. |
| 8 | `AssignAppointmentArtistCommand` implements `IAuditableCommand` with a new `AuditActions.AppointmentArtistAssigned` constant. | Follows the same convention as `ClientArtistReassigned` — a staff-roster mutation logged next to its sibling. |

**Explicitly out of scope, flagged and not fixed here:** `GetAppointmentQuery`,
`ConfirmAppointmentCommand`, `CompleteAppointmentCommand`, and `MarkNoShowCommand` have no
check that an Artist-role caller is acting on their *own* appointment — any artist at the
studio can already view, confirm, complete, or no-show a colleague's appointment today by
navigating directly to its id, since `AppointmentEndpoints.cs` only gates by rank
(`ArtistAndAbove`), not ownership. This predates this feature and isn't made worse by it — an
unassigned appointment is if anything more obviously "not yours" than a colleague's assigned
one — but it's directly adjacent. Worth its own hardening pass; not folded into this one.

---

## 4. Backend changes

### 4.1 Domain + EF Core

- `Appointment.cs`: `ArtistId` → `Guid?`; `Artist` navigation → `Artist?`.
- `AppointmentConfiguration.cs`: the existing `HasOne(a => a.Artist).WithMany(...).HasForeignKey(a => a.ArtistId)`
  needs `.IsRequired(false)`. Existing composite index `ix_appointments_studio_artist_date`
  is unaffected (NULLs group together fine for the owner's filtered queries).
- Migration: this loosens an *existing required* column rather than adding a new one — still a
  single safe migration (no existing rows need backfilling; nothing currently has a NULL
  `artist_id` to worry about). Name it `AllowNullArtistIdOnAppointment`.
- `ArtistSchedule.cs`/`ArtistTimeOff.cs` — unchanged; reused for the any-artist check below.

### 4.2 A shared "any active artist available" check

`CreateAppointmentCommand`, `CheckSlotAvailabilityQuery`, and `RescheduleAppointmentCommand`
all currently duplicate a single-artist schedule/time-off/conflict check. Rather than adding a
fourth near-duplicate for the any-artist case, extract one shared method — e.g. a static
`ArtistAvailability.IsAnyArtistAvailable(db, studioId, date, durationMinutes, ct)` in
`Domain/Services/` (same home as `DepositCalculator`) — that:

1. Loads active artists for the studio.
2. For each, checks `ArtistSchedule` (day/time window), `ArtistTimeOff`, and conflicting
   `Appointment` rows (excluding Cancelled) — same conditions as today's single-artist path.
3. Returns `true` as soon as one artist clears all three; `false` if none do.

All three call sites branch on `artistId is null` to call this instead of the existing
single-artist query. This keeps the single-artist code path completely unchanged (low risk to
the existing, working flow) and avoids triplicating the any-artist logic.

### 4.3 `CreateAppointmentCommand`

- `CreateAppointmentRequest.ArtistId` → `Guid?`.
- When `req.ArtistId is Guid artistId` (specific-artist path): **unchanged**, exactly as today.
- When `req.ArtistId is null` (studio-choice path):
  - Run §4.2's any-artist check instead of loading one `Artist`/checking one schedule.
  - Skip `ISlotLocker` entirely (decision #6).
  - `Artist? artist = null;` — `DepositCalculator.Calculate(rule, null, duration)` already
    returns the correct value for both rule types with no code change.
  - Create the `Appointment` with `ArtistId = null`.
- `CreateAppointmentValidator.cs`: drop the `NotEmpty()` rule on `ArtistId` (it's now
  legitimately nullable); no replacement rule needed.

### 4.4 New — `AssignAppointmentArtistCommand`

New file `Application/Appointments/Commands/AssignAppointmentArtistCommand.cs`, following the
`UpdateClientArtistCommand` shape:

```csharp
public record AssignAppointmentArtistCommand(Guid AppointmentId, AssignAppointmentArtistRequest Request)
    : IRequest<AppointmentResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.AppointmentArtistAssigned;
    public string AuditTargetType => AuditTargetTypes.Appointment;
    public Guid AuditTargetId => AppointmentId;
}
```

Handler responsibilities:

1. Load the appointment; 404 if missing.
2. Reject (`BusinessRuleViolationException`) if `Status` is `Cancelled`, `Completed`, or
   `NoShow` — mirrors `RescheduleAppointmentCommand`'s terminal-status guard.
3. Load and validate the target `Artist` (exists, active) — 404 / business-rule violation on
   failure, same shape as `CreateClientHandler`'s artist validation.
4. Re-run the **single-artist** schedule/time-off/conflict check (not the any-artist one) for
   this specific artist at the appointment's existing `Date`/`EndDate` — reuse the exact logic
   `CreateAppointmentCommand` already has for this, don't re-derive it.
5. Acquire the `ISlotLocker` lock for `(studioId, artistId, date)` around the conflict check +
   save, exactly as `CreateAppointmentCommand` does — this is where the real single-artist
   resource claim happens (decision #6).
6. Set `appointment.ArtistId = artist.Id`.
7. If `appointment.DepositAmount == 0m && appointment.DepositStatus == DepositStatus.Pending`:
   look up the currently-active `DepositRule` (same lookup `CreateAppointmentCommand` uses) and
   recompute via `DepositCalculator.Calculate(rule, artist.HourlyRate, appointment.DurationMinutes)`,
   persisting the result (decision #5).
8. Save, map, `realtime.NotifyStudioAsync(..., "AppointmentArtistAssigned", ...)`, and send a
   new notification (see §5) to both the client and the newly assigned artist.

New contract `AssignAppointmentArtistRequest.cs`:

```csharp
public record AssignAppointmentArtistRequest(Guid ArtistId);
```

Validator: `ArtistId` `NotEmpty()`, `AppointmentId` `NotEmpty()` — mirrors
`UpdateClientArtistValidator.cs`.

### 4.5 `ConfirmAppointmentCommand`

Add, alongside the existing `Status != Pending` check:

```csharp
if (appointment.ArtistId is null)
    throw new BusinessRuleViolationException(
        "Assign an artist before confirming this appointment.");
```

Server-side enforcement per decision #2 and this codebase's established "never trust the
frontend gate alone" convention (`CreateClientCommand`/`CreateDesignCommand` precedent).

### 4.6 `CheckSlotAvailabilityQuery` + endpoint

- `CheckSlotAvailabilityQuery.ArtistId` → `Guid?`.
- `AppointmentEndpoints.cs`'s `CheckSlotAvailability` handler: `artistId` route param becomes
  optional (nullable `Guid?`).
- Handler branches to §4.2's shared check when `ArtistId is null`.

### 4.7 `RescheduleAppointmentCommand`

Branch the conflict check:

```csharp
bool conflict = appointment.ArtistId is Guid artistId
    ? await db.Appointments.AnyAsync(a => a.Id != command.AppointmentId
        && a.ArtistId == artistId && a.Date < newEnd && a.EndDate > req.NewDate
        && a.Status != AppointmentStatus.Cancelled, ct)
    : !await ArtistAvailability.IsAnyArtistAvailable(db, tenant.StudioId, req.NewDate, req.NewDurationMinutes, ct);
```

Flagged explicitly: without this branch, `a.ArtistId == appointment.ArtistId` where
`appointment.ArtistId` is `null` would translate to `a.artist_id IS NULL` — meaning
rescheduling one unassigned appointment would spuriously conflict-check against every *other*
unassigned appointment at an overlapping time, which is wrong (they're not really competing
for the same resource; nothing has been claimed yet).

### 4.8 `GetAppointmentsQuery` / `GetAppointmentQuery` / `AppointmentResponse` / `CreateAppointmentHandler.Map`

- `AppointmentResponse.ArtistId` → `Guid?`; add `ArtistName` (`string?`, trailing optional
  param — same positional-record-compat trick `ClientResponse.ArtistName` used).
- `CreateAppointmentHandler.Map` gains an `Artist?` param (defaults `null`) and denormalizes
  `ArtistName` the same way it already does `ClientName`.
- `GetAppointmentsQuery`/`GetAppointmentQuery`: `.Include(a => a.Artist)` and pass it through
  to `Map`. No change to the existing artist-role scoping (`ArtistId == myArtistId` already
  correctly excludes unassigned appointments from an artist's own view until assigned to them).

### 4.9 API endpoint

`AppointmentEndpoints.cs`, add alongside the other `{id:guid}`-scoped routes:

```csharp
group.MapPatch("{id:guid}/artist", AssignAppointmentArtist).RequireAuthorization("OwnerOnly");
```

---

## 5. Notifications

- `SendAppointmentCreatedNotificationCommand.cs` — **verify at implementation time** (this spec
  only read the client-email portion) whether it also emails/SMS's the artist on creation. If
  so, guard that block on `appointment.ArtistId is not null` — there's no artist to notify for
  a studio-choice booking yet.
- Consider whether the Owner needs a distinct heads-up that a *new unassigned* booking landed
  and needs action, versus the existing generic `AppointmentCreated` realtime event. Minimum
  viable: reuse the existing channel with a distinguishable payload/message; a dedicated
  in-app "needs artist" notification type is a nice-to-have, not required for v1.
- New — `SendAppointmentArtistAssignedNotificationCommand`, sent from
  `AssignAppointmentArtistCommand` (§4.4 step 8): informs the client who their artist is, and
  informs the newly assigned artist of the appointment. Follow the existing
  `SendAppointmentConfirmationCommand`/`SendAppointmentCreatedNotificationCommand` pattern
  (email via `IEmailRenderer`, respects `INotificationPreferenceService`, logs via
  `NotificationLog`).

---

## 6. Frontend changes

### 6.1 `appointment.types.ts` / `appointmentsApi.ts`

- `AppointmentResponse.artistId` → `string | null`; add `artistName: string | null`.
- `CreateAppointmentRequest.artistId` → `string | null`.
- `GetAppointmentsParams.artistId` unaffected.
- New: `useAssignAppointmentArtistMutation` — `PATCH appointments/{id}/artist`,
  `invalidatesTags: [{ type: "Appointment", id }, "Appointment"]`.

### 6.2 `BookAppointmentForm.tsx`

- Replace the required artist `Select` with a two-way choice — e.g. a segmented control /
  `RadioGroup` above it: **"Choose an artist"** (default, current behavior — the existing rich
  `ArtistSelectItem` dropdown) vs **"Let the studio choose"** (new — hides the artist dropdown
  entirely, no artist-specific UI shown).
- zod schema: `artistId` becomes `z.string().nullable()`, with the "let studio choose" path
  setting it to `null` rather than requiring a selection.
- `useCheckSlotAvailabilityQuery` call: pass `artistId` only when a specific artist is chosen;
  omit it (letting the backend run the any-artist check) otherwise.
- `onSubmit`: send `artistId: values.artistId` (already `null` for the studio-choice path — no
  extra branching needed at submit time beyond the schema change).
- Copy: something like "We'll confirm an artist is available and the studio will assign one
  before your appointment is confirmed" near the toggle, so the client understands they won't
  know their artist immediately.

### 6.3 `SchedulePage.tsx` / `AppointmentCard.tsx`

- Add a small "Needs artist" badge/indicator wherever `status === Pending && artistId === null`
  — this is the Owner's primary way of spotting these in the weekly view. Exact placement
  depends on `AppointmentCard.tsx`'s current layout (not fully reviewed in this pass — read it
  in full before implementing).

### 6.4 `AppointmentDetailPage.tsx`

- Add an **Artist** `Row`, placed near the top (e.g. right after Client), following the exact
  editable/read-only split `ClientDetailPage.tsx` established yesterday for its artist field:
  - `canOwner` → an editable `Select` (options: active artists; current value
    `appt.artistId ?? "unassigned"`) wired to `useAssignAppointmentArtistMutation`. Unlike the
    client version, there is **no "Unassigned" option to select** — assignment is one-directional
    per decision #4 (only shown as the *current* state, not choosable).
  - Non-owner → plain text: artist name, or "Unassigned — the studio hasn't picked an artist
    yet" styled distinctly (e.g. muted/amber) when null.
- Guard the "Confirm appointment" button: when `appt.artistId === null`, either hide it or
  render it disabled with adjacent copy ("Assign an artist before this can be confirmed") —
  the backend now rejects this anyway (§4.5), but the button should not invite a doomed click.
- Guard the "Send Reminder" button + `ReminderDialog`'s `artistId={appt.artistId}` prop — it
  currently assumes non-null; hide/disable until an artist is assigned.

### 6.5 `MyBookingsSection.tsx`

No code change expected — `DepositArea` already reacts to `depositAmount`/`depositStatus`
changes (§2), so a deposit that goes from 0 to a real amount after assignment (decision #5)
surfaces automatically. Verify this holds once the backend change lands; flag if not.

---

## 7. Tests

**Backend**

- `CreateAppointmentHandlerTests` — new cases: studio-choice booking succeeds when at least one
  active artist is free; fails (`BusinessRuleViolationException`/equivalent) when none are;
  `ArtistId` persists as `null`; `DepositAmount` is `0` for a percent rule, correct for a fixed
  rule; no `ISlotLocker` call is made on this path (mock verification).
- New `AssignAppointmentArtistHandlerTests` — success path incl. deposit recompute when it was
  `0`; no-op on deposit when it was already nonzero (fixed-rule case); 404 on missing
  appointment/artist; business-rule violation on inactive artist, terminal-status appointment,
  and a schedule/time-off/conflict clash for the specific artist chosen.
- `ConfirmAppointmentHandlerTests` — new case: `BusinessRuleViolationException` when
  `ArtistId is null`.
- `RescheduleAppointmentHandlerTests` — new case: rescheduling an unassigned appointment
  succeeds/fails based on any-artist availability at the new time, not a spurious
  null-vs-null conflict against another unassigned appointment.
- `CheckSlotAvailabilityHandlerTests` — new cases for `ArtistId: null`.
- Integration test: `PATCH /api/v1/appointments/{id}/artist` — 200 for Owner, 403 for Artist.

**Frontend**

- `BookPage.test.tsx` — new cases: submitting via "let the studio choose" (no artist selection)
  succeeds; the artist dropdown is hidden in that mode; the slot-check call omits `artistId`.
- `AppointmentDetailPage.test.tsx` — Owner sees an editable artist Select and can assign one;
  Confirm is disabled/hidden pre-assignment; non-owner sees read-only "Unassigned" text.
- `SchedulePage.test.tsx` — needs-artist badge renders for the right appointments.
- Update any `AppointmentResponse`-shaped fixtures across the appointments/payments test
  suites for the new `artistName` field and nullable `artistId` (check via `tsc`/test-run
  failures, not by hand-enumerating files, per this codebase's own established pattern).

---

## 8. Help Menu, user manual, onboarding tour (CLAUDE.md rule 7)

- `helpContent.ts`:
  - `client-book-appointment` (existing): update `steps` to mention the studio-choice option;
    add a tip explaining the client won't know their artist until the studio assigns one.
  - `owner-schedule` (existing): add a step/tip about spotting and assigning "needs artist"
    bookings.
  - New article, e.g. `owner-appointments-assign-artist`: how to assign an artist to a
    studio-choice booking from the appointment detail page.
- `frontend/public/user-manual/index.html`: update the `#owner-create-client`-equivalent
  booking section's wireframe/steps (exact section id to be located at implementation time —
  not identified in this pass) and the owner-facing appointment detail section.
- `clientTour.ts`: the existing `client-book-nav` step's body ("pick an artist, a date, and how
  long the session should be") should be updated to mention the alternative. `ownerTour.ts` has
  no appointment-detail or schedule step today — a new step is optional, not required, for
  this pass (genuine no-op candidate, verify before assuming).

---

## 9. Open questions — need product-owner sign-off before this becomes an overnight prompt

1. **Decision #3** (no new status, computed "needs artist" state) — confirm this is acceptable,
   or a stored status is preferred for easier querying/filtering later.
2. **Decision #5** (recompute + auto-collect deposit at assignment time) — confirm this is the
   desired payment behavior, versus e.g. never charging a deposit for studio-choice bookings,
   or requiring the Owner to manually trigger a charge rather than it becoming payable
   automatically via the existing reactive `DepositArea`.
3. Should the Owner get a dedicated "needs artist" queue/list (e.g. on the dashboard), or is a
   badge on `SchedulePage.tsx`/`AppointmentCard.tsx` sufficient for v1? This spec scopes to the
   badge; a dashboard addition is a larger, separate lift.
4. Confirm `SendAppointmentCreatedNotificationCommand`'s full contents (only partially read in
   this pass) before writing the exact guard/diff for it.
5. Confirm whether `AppointmentCard.tsx`'s current layout has room for a "needs artist" badge
   without a larger redesign (not fully reviewed in this pass).

---

## 10. Definition of done (for the eventual overnight prompt)

- [ ] All open questions in §9 resolved and folded into an updated Decisions table.
- [ ] Migration applied cleanly; `dotnet ef database update` succeeds; app boots.
- [ ] `dotnet build` / `dotnet test` — zero errors, all green, including every new test in §7.
- [ ] `pnpm tsc --noEmit` / `pnpm test` — zero errors, all green.
- [ ] Manual smoke check: a client can book a specific artist (unchanged) or "let the studio
      choose"; a studio-choice booking with no available artist is rejected at submit time; an
      Owner can assign an artist to a Pending unassigned booking; Confirm is blocked until
      then, both in the UI and server-side; a deferred percent-rule deposit becomes payable
      once an artist is assigned.
- [ ] `helpContent.ts`, `user-manual/index.html`, `clientTour.ts` updated per §8; `ownerTour.ts`
      confirmed (not just assumed) to need no change.
