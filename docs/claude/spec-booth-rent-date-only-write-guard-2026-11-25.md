# Spec: the booth-rent write path should store a date, not an instant

- **Date:** 2026-11-25
- **Author:** Claude Code, at Desktop's instruction (`desktop-instructions-rulings-and-gate-fixes-2026-11-25.md` §6)
- **Status:** BUILT 2026-11-26 as recommended in §4 point 2 (`DateOnly` in both request contracts, `BoothRentDates.ToChargeDate`).
  The §5 count was still owed when the PR opened; its result is in the PR and decides whether anything beyond the contract is needed.
- **Read at:** `origin/main` after #461 (`00c3a542`)

## 1. Why this exists

Since 25 Nov both booth-rent screens print `NextChargeDate` and `ChargedDate` as **date-only** values
(`formatDateOnly`, UTC). See the Decisions Log entry "Booth-rent dates are date-only". That is correct for every value the
UI can produce. The UI sends UTC midnight, and the job only adds whole days or months.

**The API does not enforce it.** A caller other than the form can store a time of day, and for that row the screens
would then print a date that is not the one the caller meant.

## 2. Current behaviour

| Step | Where | What happens to the value |
|---|---|---|
| Owner picks a date | `BoothRentManagementPage.tsx` `type="date"` | `"YYYY-MM-DD"` |
| Sent | same file, `new Date(nextChargeDate).toISOString()` | `"YYYY-MM-DDT00:00:00.000Z"` (a date-only string parses as UTC midnight) |
| Bound | `CreateBoothRentScheduleRequest` / `UpdateBoothRentScheduleRequest`, `DateTime NextChargeDate` | System.Text.Json. A `Z` value gives `Kind=Utc`, a bare date gives `Unspecified`, and an **offset** (`+01:00`) gives `Kind=Local`, already converted to the server's local time |
| Validated | `CreateBoothRentScheduleValidator`, `UpdateBoothRentScheduleValidator` | **No rule on `NextChargeDate` at all**: no time-of-day check and no range |
| Stored | `CreateBoothRentScheduleCommand:33`, `UpdateBoothRentScheduleCommand:30` copy it unchanged; `UtcDateTimeConverter` turns `Local` into UTC and labels `Unspecified` as UTC | whatever instant arrived |
| Advanced | `BoothRentChargeJob:51-57`: `ChargedDate = NextChargeDate`, then `AddDays(7)` / `AddMonths(1)` | the time of day is carried into every later charge |

**How a row goes wrong:** a client that sends `2026-12-01T00:00:00+01:00` (a date at midnight in Tirane) is stored as
`2026-11-30T23:00:00Z`. `formatDateOnly` then prints **30 Nov**, one day early, and so does every charge the job
creates from that schedule afterwards. A value like `2026-12-01T15:30:00Z` prints the right day but is not a date,
and the job makes it due at 15:30 UTC rather than at the start of the day.

## 3. Affected endpoints

| Endpoint | Policy | Caller today |
|---|---|---|
| `POST /api/v1/booth-rent/schedules` | `OwnerOnly` | the New schedule dialog |
| `PUT /api/v1/booth-rent/schedules/{id}` | `OwnerOnly` | **none in the UI**: `useUpdateBoothRentScheduleMutation` is exported from `features/booth-rent/index.ts` and never called |

`POST /charges/{id}/settle` does not take a date. `ChargedDate` is written only by the job, from `NextChargeDate`, so
guarding the schedule's two write sites guards both columns.

## 4. Proposed change

1. **Normalise rather than reject, in the commands, with the rule in one place.** Add a small helper, e.g.
   `BoothRentDates.ToChargeDate(DateTime value)`, returning `DateTime.SpecifyKind(value.Date, DateTimeKind.Utc)` taken
   on the **calendar date the caller wrote**. That needs the date before any zone conversion, so the binding has to
   change too (point 2). Both commands call it.
2. **Make the contract say what it means.** Change the two request records' `NextChargeDate` to `DateOnly`, which
   System.Text.Json binds from `"YYYY-MM-DD"` and rejects with a 400 when given a time. Then an offset cannot be
   converted away before the handler sees it, and the helper becomes `value.ToDateTime(TimeOnly.MinValue,
   DateTimeKind.Utc)`. The frontend sends the input's own `"YYYY-MM-DD"` instead of `toISOString()`. **This is the
   recommendation**, because it removes the ambiguity at the boundary rather than guessing which date an offset
   meant.
   - The alternative (keep `DateTime`, truncate to `.Date` after conversion) is simpler but **reproduces the bug in
     §2**. `+01:00` midnight has already become 23:00 the previous day by then, and truncating keeps the wrong day.
3. **Validators:** with `DateOnly` there is nothing left to check about the time. A range rule (not before, say,
   the studio's creation date) is a separate product question and is **not** part of this change.
4. **Entity and column stay `DateTime`.** Moving the column to MySQL `DATE` is possible but is a migration of a
   money-adjacent table for no behaviour change, so it is not proposed.
5. **Tests:** handler tests for both commands (a date stores as UTC midnight, the job advances it and stays at
   midnight), and an endpoint test that `"2026-12-01T15:30:00Z"` is a 400. Mutation check: drop the helper call and
   the stored-midnight test must fail.
6. **Help:** no change. The screens already say charge dates are calendar dates.

## 5. The migration question

Rows that already carry a time of day are the open part, and they need a decision, not a script run on sight.

- **Count first, read-only.** Not run for this spec:
  ```sql
  SELECT 'schedules' AS t, COUNT(*) FROM BoothRentSchedules WHERE TIME(NextChargeDate) <> '00:00:00'
  UNION ALL
  SELECT 'charges', COUNT(*) FROM BoothRentCharges WHERE TIME(ChargedDate) <> '00:00:00';
  ```
  On production this goes through the one-off read procedure with Phi. Every UI-created row is UTC midnight, and
  the update endpoint has no UI caller, so **zero is the expected answer**.
- **If zero:** no data migration. The change is the contract only.
- **If not zero,** each row needs its intended date decided. `23:00` usually means midnight the next day in a
  UTC+1 zone, while `15:30` probably means its own date. That is a guess for each row, and on `ChargedDate` it is a
  bookkeeping record an owner may already have settled. So **do not rewrite `ChargedDate` on settled charges**. List
  them for the owner instead, and normalise only future `NextChargeDate` values, with the before and after recorded.

## 6. Related, not in scope

- `BoothRentChargeJob` runs daily at 10:00 UTC and compares `NextChargeDate <= now`. So a charge dated 1 Dec is
  created at 10:00 UTC on 1 Dec, which is 05:00 in New York and 11:00 in Tirane. The **date printed** is right in
  every zone. The **moment it appears** is a separate question no one has raised, and nothing reconciles by it.
- The update endpoint having no UI is a product gap (an owner cannot edit or deactivate a schedule), not part of
  this guard.
