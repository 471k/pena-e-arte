# Overnight Prompt — Reschedule Appointment: Frontend UI

**Date:** 2026-07-18
**Files changed:** ~9 (7 frontend edits/new files + 2 test files)
**Type:** Feature (frontend-only — backend is already complete and fully tested)

---

## Context

`RescheduleAppointmentCommand` / `RescheduleAppointmentHandler` (`Pena_e_Arte.Application/Appointments/Commands/RescheduleAppointmentCommand.cs`)
and the `PATCH /api/v1/appointments/{id}/reschedule` endpoint (`AppointmentEndpoints.cs:25`, policy `ArtistAndAbove`) have existed since the
artist QA pass and are fully covered by `tests/Pena_e_Arte.UnitTests/Appointments/RescheduleAppointmentHandlerTests.cs` (7 passing tests:
pending/confirmed reschedule, realtime notification, not-found, cancelled/completed rejection, slot-conflict rejection). Nothing in the
frontend calls it — there is no mutation hook in `appointmentsApi.ts`, no button anywhere, no dialog. This prompt closes that gap. **Do not
touch the backend** — it does not need any changes for this prompt.

### What the backend actually does (read this before writing UI copy)

```csharp
// RescheduleAppointmentRequest — Pena_e_Arte.Contracts/Requests/RescheduleAppointmentRequest.cs
public record RescheduleAppointmentRequest(DateTime NewDate, int NewDurationMinutes, string? Notes);
```

- Rejects with `BusinessRuleViolationException` (422) if the appointment is `Cancelled`, `Completed`, or `NoShow` — reschedule is only
  valid for `Pending`/`Confirmed` appointments. The handler message is literally `"Cannot reschedule a {Status} appointment."`
- Rejects with `SlotAlreadyBookedException` (409) if the new date/duration overlaps another non-cancelled appointment for the same artist.
- Validator (`RescheduleAppointmentValidator`) requires `NewDate` to be in the future and `NewDurationMinutes` between 30 and 480 —
  identical bounds to `CreateAppointmentValidator`.
- On success it fires `NotifyStudioAsync(tenant.StudioId, "AppointmentUpdated", response, ct)` over SignalR — the existing
  `AppointmentUpdated` event name is already wired into `useSignalR` conventions per `architecture.md`'s SignalR table; no new event
  handling is needed on the frontend, RTK Query cache invalidation (below) is sufficient since nothing currently subscribes to
  `AppointmentUpdated` client-side.
- **Authorization is `ArtistAndAbove` only.** Clients cannot call this endpoint. This prompt does **not** add a client-facing
  "request a new time" flow — that would need a new backend command with different authorization and business rules (e.g. does it
  need artist approval? does it re-trigger deposit collection?) and is explicitly out of scope. See Forbidden Actions.

---

## Phase 0 — Required Reading

```
Pena_e_Arte.Application/Appointments/Commands/RescheduleAppointmentCommand.cs
Pena_e_Arte.Contracts/Requests/RescheduleAppointmentRequest.cs
Pena_e_Arte.API/Endpoints/AppointmentEndpoints.cs                      — line 25, the reschedule route
tests/Pena_e_Arte.UnitTests/Appointments/RescheduleAppointmentHandlerTests.cs

frontend/src/features/appointments/appointment.types.ts
frontend/src/features/appointments/appointmentsApi.ts
frontend/src/features/appointments/components/AppointmentCard.tsx      — Cancel/Confirm/Complete/NoShow inline-action pattern
frontend/src/features/appointments/components/AppointmentDetailPage.tsx — Cancel confirmation Dialog pattern (clone this structure)
frontend/src/features/appointments/components/BookAppointmentForm.tsx  — DURATION_OPTIONS list, datetime-local input pattern,
                                                                          debounced slot-availability check (SlotAvailabilityIndicator,
                                                                          currently defined locally in this file — Phase 1 extracts it)
frontend/src/features/appointments/components/SchedulePage.tsx         — confirms AppointmentCard is the only render path here;
                                                                          no changes needed to this file
frontend/src/features/appointments/components/AppointmentStatusBadge.tsx
frontend/src/shared/components/ui/dialog.tsx
frontend/src/shared/hooks/usePermission.ts
docs/claude/conventions.md
```

---

## Phase 1 — Extract `SlotAvailabilityIndicator` to a shared component

### Why

`BookAppointmentForm.tsx` already has a `SlotAvailabilityIndicator` sub-component (checking/available/unavailable states) and a debounced
`useCheckSlotAvailabilityQuery` pattern. The reschedule dialog needs the exact same check (is the *new* slot free for this artist) before
letting the user submit — duplicating ~40 lines of debounce + indicator logic would be a real DRY violation now that two components need
it (it wasn't worth extracting when only one did).

### 1a — New file `frontend/src/features/appointments/components/SlotAvailabilityIndicator.tsx`

Move this function **verbatim** out of `BookAppointmentForm.tsx` (currently lines 152–184):

```tsx
import { AlertCircle, CheckCircle2, Loader2 } from "lucide-react";
import type { SlotAvailabilityResponse } from "../appointment.types";

export function SlotAvailabilityIndicator({
  checking,
  status,
}: {
  checking: boolean;
  status:   SlotAvailabilityResponse | undefined;
}) {
  if (checking) {
    return (
      <p className="flex items-center gap-1.5 text-xs text-muted-foreground">
        <Loader2 className="h-3 w-3 animate-spin" aria-hidden="true" />
        Checking availability…
      </p>
    );
  }
  if (!status) return null;

  if (status.available) {
    return (
      <p className="flex items-center gap-1.5 text-xs text-green-600 dark:text-green-400">
        <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
        This slot is available
      </p>
    );
  }

  return (
    <p className="flex items-center gap-1.5 text-xs text-destructive" role="alert">
      <AlertCircle className="h-3.5 w-3.5" aria-hidden="true" />
      {status.reason ?? "This slot is not available."}
    </p>
  );
}
```

### 1b — `BookAppointmentForm.tsx`

- Delete the local `SlotAvailabilityIndicator` function definition (lines 152–184).
- Add `import { SlotAvailabilityIndicator } from "./SlotAvailabilityIndicator";`
- Remove `AlertCircle, CheckCircle2, Loader2` from the top-level lucide-react import **only if** they become unused after the
  extraction — `AlertCircle` and `Loader2` are still used elsewhere in this file (studio-switch error states, submit button spinner),
  so only drop what's actually dead. Check with a find-usages pass before deleting any icon import.
- No other changes to `BookAppointmentForm.tsx` — it already renders `<SlotAvailabilityIndicator checking={checkingSlot} status={slotStatus} />` at the call site; that JSX is unchanged, only the import source changes.

### Tests

No new tests needed for this phase — `BookAppointmentForm.test.tsx`'s existing slot-availability assertions (if any; check
`frontend/src/features/appointments/__tests__/BookPage.test.tsx`) continue to exercise the same component through the same public props,
just from a different import path. If lint or `pnpm test` show any breakage from the extraction, fix the import, don't rewrite the tests.

---

## Phase 2 — `appointmentsApi.ts` + `appointment.types.ts`

### 2a — `appointment.types.ts`

Add the request type (mirrors the backend `RescheduleAppointmentRequest` record exactly):

```typescript
export interface RescheduleAppointmentRequest {
  newDate:            string;
  newDurationMinutes: number;
  notes:              string | null;
}
```

Place it directly below `CreateAppointmentRequest` — same shape convention (string ISO date, not `Date`).

### 2b — `appointmentsApi.ts`

```typescript
import type {
  AppointmentResponse,
  CheckSlotAvailabilityParams,
  CreateAppointmentRequest,
  GetAppointmentsParams,
  RescheduleAppointmentRequest,   // ← add
  SlotAvailabilityResponse,
} from "./appointment.types";
```

Add the mutation, placed after `markNoShow` (matches the backend route-declaration order in `AppointmentEndpoints.cs`):

```typescript
    markNoShow: builder.mutation<AppointmentResponse, string>({
      query: (id) => ({ url: `appointments/${id}/no-show`, method: "PATCH" }),
      invalidatesTags: ["Appointment"],
    }),
    rescheduleAppointment: builder.mutation<
      AppointmentResponse,
      { id: string } & RescheduleAppointmentRequest
    >({
      query: ({ id, ...body }) => ({
        url:    `appointments/${id}/reschedule`,
        method: "PATCH",
        body,
      }),
      invalidatesTags: ["Appointment"],
    }),
```

Add `useRescheduleAppointmentMutation` to the exported hooks block at the bottom, after `useMarkNoShowMutation`.

**Do not** add `rescheduleAppointment` as a separate tag or give it special cache-invalidation behavior — `invalidatesTags: ["Appointment"]`
is exactly what every other appointment-mutating endpoint in this file already does (`cancelAppointment`, `confirmAppointment`,
`completeAppointment`, `markNoShow`) and is sufficient: it refetches whichever `getAppointments`/`getAppointment`/`getMyAppointments`
query is currently mounted (`SchedulePage`, `AppointmentDetailPage`).

---

## Phase 3 — New `RescheduleDialog.tsx`

New file: `frontend/src/features/appointments/components/RescheduleDialog.tsx`

This is a controlled dialog (parent owns `open`/`onOpenChange`), modeled directly on `AppointmentDetailPage.tsx`'s existing Cancel
`Dialog` (same `Dialog`/`DialogContent`/`DialogHeader`/`DialogTitle`/`DialogDescription`/`DialogFooter` primitives), with a form body
borrowed from `BookAppointmentForm.tsx`'s date/duration/slot-check fields.

```tsx
import { useEffect, useState } from "react";
import { Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/shared/components/ui/select";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter,
  DialogHeader, DialogTitle,
} from "@/shared/components/ui/dialog";
import { SlotAvailabilityIndicator } from "./SlotAvailabilityIndicator";
import { useRescheduleAppointmentMutation, useCheckSlotAvailabilityQuery } from "../appointmentsApi";
import type { AppointmentResponse } from "../appointment.types";

// Same bounds as RescheduleAppointmentValidator (30–480) and BookAppointmentForm's DURATION_OPTIONS.
const DURATION_OPTIONS: { value: number; label: string }[] = [
  { value: 30,  label: "30 min — Touch-up" },
  { value: 45,  label: "45 min" },
  { value: 60,  label: "1 hour" },
  { value: 90,  label: "1.5 hours" },
  { value: 120, label: "2 hours" },
  { value: 180, label: "3 hours" },
  { value: 240, label: "4 hours" },
  { value: 300, label: "5 hours" },
  { value: 360, label: "6 hours" },
  { value: 480, label: "Full day (8 h)" },
];

interface RescheduleDialogProps {
  appointment: AppointmentResponse;
  open:        boolean;
  onOpenChange: (open: boolean) => void;
}

// Formats an ISO datetime string for an <input type="datetime-local"> value (local time, no seconds).
function toDatetimeLocalValue(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export function RescheduleDialog({ appointment, open, onOpenChange }: RescheduleDialogProps) {
  const [newDate, setNewDate] = useState(() => toDatetimeLocalValue(appointment.date));
  const [newDuration, setNewDuration] = useState(appointment.durationMinutes);
  const [reschedule, { isLoading }] = useRescheduleAppointmentMutation();

  // Reset the form to the appointment's current values every time the dialog is (re)opened —
  // otherwise a cancelled edit followed by reopening shows stale draft values.
  useEffect(() => {
    if (open) {
      setNewDate(toDatetimeLocalValue(appointment.date));
      setNewDuration(appointment.durationMinutes);
    }
  }, [open, appointment.date, appointment.durationMinutes]);

  // Debounced slot-availability check for the *new* slot — same 600ms pattern as BookAppointmentForm.
  // Excludes the appointment's own current slot from counting as a "conflict" by construction: the
  // backend's conflict check already excludes `a.Id != command.AppointmentId`, so re-submitting the
  // unchanged slot correctly reports available.
  const [debouncedCheck, setDebouncedCheck] = useState<{ artistId: string; date: string; durationMinutes: number } | null>(null);
  useEffect(() => {
    if (!open) { setDebouncedCheck(null); return; }
    const timer = setTimeout(() => {
      if (!newDate || !newDuration) { setDebouncedCheck(null); return; }
      setDebouncedCheck({ artistId: appointment.artistId, date: new Date(newDate).toISOString(), durationMinutes: newDuration });
    }, 600);
    return () => clearTimeout(timer);
  }, [open, newDate, newDuration, appointment.artistId]);

  const { data: slotStatus, isFetching: checkingSlot } = useCheckSlotAvailabilityQuery(debouncedCheck!, {
    skip: debouncedCheck === null,
  });

  async function handleSubmit() {
    const result = await reschedule({
      id:                 appointment.id,
      newDate:            new Date(newDate).toISOString(),
      newDurationMinutes: newDuration,
      notes:              appointment.notes,
    });
    if ("data" in result) {
      toast.success("Appointment rescheduled.");
      onOpenChange(false);
    } else {
      const errMsg =
        (result.error as { data?: { message?: string } } | undefined)?.data?.message
        ?? "Failed to reschedule appointment.";
      toast.error(errMsg);
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Reschedule appointment</DialogTitle>
          <DialogDescription>
            Pick a new date, time, and duration. The client is not automatically notified — let them know separately.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-3 py-2">
          <div className="space-y-1.5">
            <Label htmlFor="reschedule-date">New date &amp; time</Label>
            <Input
              id="reschedule-date"
              type="datetime-local"
              min={new Date().toISOString().slice(0, 16)}
              value={newDate}
              onChange={(e) => setNewDate(e.target.value)}
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="reschedule-duration">Duration</Label>
            <Select value={String(newDuration)} onValueChange={(v) => setNewDuration(Number(v))}>
              <SelectTrigger id="reschedule-duration">
                <SelectValue placeholder="Select duration" />
              </SelectTrigger>
              <SelectContent>
                {DURATION_OPTIONS.map(({ value, label }) => (
                  <SelectItem key={value} value={String(value)}>{label}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {debouncedCheck !== null && (
            <SlotAvailabilityIndicator checking={checkingSlot} status={slotStatus} />
          )}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isLoading}>
            Cancel
          </Button>
          <Button
            onClick={handleSubmit}
            disabled={isLoading || slotStatus?.available === false}
          >
            {isLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : "Confirm reschedule"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
```

### Design notes (read before deviating)

- **`notes` is passed through unchanged** (`appointment.notes`), not editable in this dialog. The backend request shape includes
  `Notes` because it reuses the same field the appointment already has, but this dialog's job is only to change *when*, not the notes —
  adding a notes-edit UI here would silently let staff overwrite consultation notes without meaning to. If a future prompt wants
  notes-editing during reschedule, that is a deliberate, separate decision — do not add it speculatively here.
- **No client notification is sent.** The backend does not email/SMS the client on reschedule (only `AppointmentUpdated` over
  SignalR, which is an internal realtime event for the studio's own staff view, not a client-facing notification path — check
  `Pena_e_Arte.Application/Notifications/` if you're unsure, but do not add a `SendAppointmentRescheduledNotificationCommand` in this
  prompt; that's backend work and explicitly out of scope). The dialog copy says so explicitly (`DialogDescription`) so staff aren't
  surprised the client wasn't auto-notified.
- **Submit is disabled when the checked slot is explicitly unavailable** (`slotStatus?.available === false`), matching
  `BookAppointmentForm`'s `disabled={isLoading || slotStatus?.available === false}` pattern exactly — but is NOT blocked while the
  check is still in flight or hasn't run yet (e.g. user hasn't paused typing for 600ms), also matching that existing convention.

---

## Phase 4 — Wire the dialog in

### 4a — `AppointmentDetailPage.tsx`

Add local state, the mutation trigger's dialog open/close, and a "Reschedule" button placed **between** the Confirm/Complete button
and the "Charge deposit" button in the `isArtistPlus && !isTerminal` action block (i.e. right after the `isConfirmed` block, before
the `depositStatus === Pending && canOwner` block) — reschedule ranks as a normal-priority action, not destructive (unlike Cancel,
which stays last with the muted/destructive styling it already has).

```tsx
import { CalendarClock, ... } from "lucide-react";   // add CalendarClock to the existing import line
import { RescheduleDialog } from "./RescheduleDialog"; // add
```

```tsx
const [rescheduleDialogOpen, setRescheduleDialogOpen] = useState(false);
```

```tsx
{/* Insert directly after the isConfirmed "Mark as complete" block, before the Charge deposit block */}
<Button
  variant="outline"
  className="w-full gap-2"
  disabled={anyLoading}
  onClick={() => setRescheduleDialogOpen(true)}
>
  <CalendarClock className="h-4 w-4" />
  Reschedule
</Button>
```

At the bottom of the component, alongside the existing Cancel `<Dialog>`:

```tsx
{appt && (
  <RescheduleDialog
    appointment={appt}
    open={rescheduleDialogOpen}
    onOpenChange={setRescheduleDialogOpen}
  />
)}
```

Guard with `appt &&` since `RescheduleDialog` requires a non-null `AppointmentResponse` prop and `appt` can be `undefined` while loading.

### 4b — `AppointmentCard.tsx`

Add a compact icon-only "Reschedule" button to the inline action row, matching the existing `Mark no-show` icon-button styling
(`h-7 px-2 text-xs gap-1 text-muted-foreground`, ghost variant) — placed **before** the "Mark no-show" button so the row reads
Confirm/Complete → Reschedule → No-show → Charge → Cancel (routine actions first, destructive/terminal actions last):

```tsx
import { CalendarClock, Check, CreditCard, Loader2, Trash2, UserX } from "lucide-react"; // add CalendarClock
import { RescheduleDialog } from "./RescheduleDialog"; // add
```

```tsx
const [rescheduleDialogOpen, setRescheduleDialogOpen] = useState(false);
```

```tsx
{/* Insert before the existing "!isPending && !isTerminal" Mark-no-show button */}
<Button
  variant="ghost"
  size="sm"
  disabled={anyLoading}
  onClick={() => setRescheduleDialogOpen(true)}
  className="h-7 w-7 p-0 text-muted-foreground"
  title="Reschedule"
  aria-label="Reschedule appointment"
>
  <CalendarClock className="h-3.5 w-3.5" />
</Button>
```

At the end of the component's returned JSX, alongside the `<Card>`:

```tsx
<RescheduleDialog
  appointment={appointment}
  open={rescheduleDialogOpen}
  onOpenChange={setRescheduleDialogOpen}
/>
```

Note the button click handler in `AppointmentCard` sits inside the `onClick={(e) => e.stopPropagation()}` wrapper `div` that already
wraps every other action button in this card (the whole card is itself a click target that navigates to the detail page) — no change
needed there, just add the button inside the existing wrapper alongside the others.

---

## Phase 5 — Tests

### 5a — `AppointmentDetailPage.test.tsx` (existing file — extend it)

Add a `PATCH .../reschedule` handler to the existing MSW `server`:

```typescript
http.patch("http://localhost/api/v1/appointments/:id/reschedule", async ({ params, request }) => {
  const body = await request.json() as { newDate: string; newDurationMinutes: number };
  return HttpResponse.json({
    ...APPT_PENDING, id: params.id as string,
    date: body.newDate, durationMinutes: body.newDurationMinutes,
  });
}),
```

New tests, appended after the existing "Confirm / Complete / No-show mutations" block:

```typescript
// ── Reschedule ──────────────────────────────────────────────────────────────

it("artist sees 'Reschedule' button for a non-terminal appointment", async () => {
  renderPage("appt-001", Role.Artist);
  expect(await screen.findByRole("button", { name: /^reschedule$/i })).toBeInTheDocument();
});

it("artist does NOT see 'Reschedule' for a Completed appointment", async () => {
  renderPage("appt-003", Role.Artist);
  await screen.findByText("90 min");
  expect(screen.queryByRole("button", { name: /^reschedule$/i })).not.toBeInTheDocument();
});

it("client role does NOT see 'Reschedule'", async () => {
  renderPage("appt-001", Role.Client);
  await screen.findByText("90 min");
  expect(screen.queryByRole("button", { name: /^reschedule$/i })).not.toBeInTheDocument();
});

it("clicking 'Reschedule' opens the reschedule dialog pre-filled with the current date and duration", async () => {
  const user = userEvent.setup();
  renderPage("appt-001", Role.Artist);
  await user.click(await screen.findByRole("button", { name: /^reschedule$/i }));

  expect(screen.getByRole("dialog")).toBeInTheDocument();
  expect(screen.getByText(/reschedule appointment/i)).toBeInTheDocument();
  expect(screen.getByLabelText(/new date/i)).toHaveValue(toDatetimeLocalValueForTest(FUTURE));
});

it("dialog 'Cancel' button closes without calling the mutation", async () => {
  const user = userEvent.setup();
  renderPage("appt-001", Role.Artist);
  await user.click(await screen.findByRole("button", { name: /^reschedule$/i }));
  const dialog = screen.getByRole("dialog");
  await user.click(within(dialog).getByRole("button", { name: /^cancel$/i }));
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
});

it("submitting a new duration calls the reschedule mutation and closes the dialog", async () => {
  const user = userEvent.setup();
  renderPage("appt-001", Role.Artist);
  await user.click(await screen.findByRole("button", { name: /^reschedule$/i }));

  const durationTrigger = screen.getByLabelText(/duration/i);
  await user.click(durationTrigger);
  await user.click(await screen.findByRole("option", { name: /1 hour$/i }));

  await user.click(screen.getByRole("button", { name: /confirm reschedule/i }));

  await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
});
```

Add a small local helper near the top of the test file (mirrors the component's own `toDatetimeLocalValue`, needed because the test
asserts against the rendered `<input>` value):

```typescript
function toDatetimeLocalValueForTest(iso: string): string {
  const d = new Date(iso);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}
```

Also add the `GET .../check-slot` handler to the server (needed because the dialog debounces a live availability check — without a
handler, `onUnhandledRequest: "error"` will fail every reschedule-dialog test once the 600ms debounce fires):

```typescript
http.get("http://localhost/api/v1/appointments/check-slot", () =>
  HttpResponse.json({ available: true, reason: null }),
),
```

### 5b — New `frontend/src/features/appointments/__tests__/AppointmentCard.test.tsx`

No test file exists for `AppointmentCard` today (it's only exercised indirectly via `SchedulePage.test.tsx`). Create one — check
`SchedulePage.test.tsx` first for the seed-data shape and MSW/store setup pattern already used for appointments in that file, and
reuse it rather than inventing a new one. Minimum required coverage for this prompt:

- Reschedule icon button is visible to artist role, hidden for a terminal-status appointment, hidden for client role (mirror the three
  `AppointmentDetailPage` tests above).
- Clicking the Reschedule icon button opens the dialog and **does not** navigate to the detail page (regression test for the
  `stopPropagation` wrapper — click the button, then assert `navigate` was not called / the detail route did not render).

### 5c — `SlotAvailabilityIndicator` extraction (Phase 1)

Run whatever test currently exercises `BookAppointmentForm`'s slot-availability states (search `frontend/src/features/appointments/__tests__/`
for assertions matching `/checking availability/i`, `/this slot is available/i`, or `/this slot is not available/i`) and confirm they
still pass unchanged after the import-path change. Do not duplicate these tests for the new file location — the component didn't move
logic, only location.

---

## Phase 6 — Quality Gates

```bash
pnpm --filter frontend test -- --reporter=verbose 2>&1 | grep -E "(PASS|FAIL|✓|✗)"
pnpm --filter frontend lint 2>&1 | grep -E "^.*error" | head -20
pnpm --filter frontend build
```

No backend build/test needed — nothing in `Pena_e_Arte.*` or `tests/Pena_e_Arte.*Tests` changes in this prompt.

---

## Phase 7 — Forbidden Actions

- **Do not modify any backend file.** `RescheduleAppointmentCommand.cs`, `RescheduleAppointmentRequest.cs`, `AppointmentEndpoints.cs`,
  and `RescheduleAppointmentHandlerTests.cs` are complete and already tested. If you find yourself wanting to change backend
  authorization, validation bounds, or the conflict-check logic to make the frontend easier, stop — that's a signal the frontend
  design is wrong, not the backend.
- **Do not add a client-facing reschedule-request flow.** The endpoint is `ArtistAndAbove`-only by design. A client "request new time"
  feature is a different, larger feature (needs artist approval semantics, possibly a new `AppointmentStatus`, and a decision on
  whether it re-triggers deposit handling) — out of scope, not a small addition to this prompt.
- **Do not add a client notification (email/SMS) on reschedule.** The dialog copy explicitly tells staff the client isn't notified
  instead of silently building a notification path that doesn't match what the backend actually does.
- **Do not make `notes` editable in the reschedule dialog.** It is passed through unchanged.
- **Do not add new npm packages.** `datetime-local` input + the existing `Select` primitive covers this; no date-picker library needed
  (matches the existing convention in `BookAppointmentForm.tsx`).
- **Do not touch `SchedulePage.tsx`.** It already renders `AppointmentCard` for every appointment in view; Phase 4b's change to
  `AppointmentCard` is sufficient for the Schedule page to pick up Reschedule automatically.
- **Do not build a drag-and-drop / calendar-grid reschedule interaction.** This prompt is deliberately a form-in-a-dialog, matching
  every other appointment-mutating action in this codebase (Cancel, Confirm, Complete, No-show all use single-click or a simple dialog
  — no drag interactions exist anywhere in this app today).

---

## Completion Checklist

- [ ] Phase 1 — `SlotAvailabilityIndicator.tsx` extracted to its own file; `BookAppointmentForm.tsx` imports it, local definition removed
- [ ] Phase 2 — `RescheduleAppointmentRequest` type added to `appointment.types.ts`
- [ ] Phase 2 — `rescheduleAppointment` mutation + `useRescheduleAppointmentMutation` exported from `appointmentsApi.ts`
- [ ] Phase 3 — `RescheduleDialog.tsx` created: pre-fills current date/duration, debounced slot check, disabled submit when slot
      explicitly unavailable, `notes` passed through unchanged, no client-notification copy implies anything false
- [ ] Phase 4a — `AppointmentDetailPage.tsx`: "Reschedule" button visible only for `isArtistPlus && !isTerminal`, positioned between
      Confirm/Complete and Charge deposit
- [ ] Phase 4b — `AppointmentCard.tsx`: icon-only Reschedule button in the inline action row, positioned before Mark-no-show,
      click does not trigger card navigation
- [ ] Phase 5a — `AppointmentDetailPage.test.tsx` extended with reschedule tests + `check-slot`/`reschedule` MSW handlers
- [ ] Phase 5b — New `AppointmentCard.test.tsx` created with Reschedule visibility + non-navigation tests
- [ ] Phase 5c — Existing slot-availability tests (wherever they live) still pass after the Phase 1 extraction
- [ ] No backend files touched
- [ ] No new npm packages
- [ ] `pnpm test`, `pnpm lint`, `pnpm build` all clean
