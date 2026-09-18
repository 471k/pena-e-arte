# Overnight Master Prompt — P1 Backlog, Group 2 ("Foundational")

**Date:** 2026-09-09
**Mode:** Fully autonomous. No user present. Run until every phase exits clean.
**Run with:** `claude --dangerously-skip-permissions`
**Before starting:** `git add -A && git commit -m "chore: pre-P1-group2 checkpoint"` then
`git checkout -b feat/p1-group2-studio-hours-timezone-2026-09-09`

---

## Context — read this before anything else

Source: `docs/claude/p1-backlog-master-build-spec-2026-09-09.md` (the full 19-item backlog) and
its "suggested build order," Group 2 — "foundational, feeds other items." This is the second of
several planned master prompts; the first
(`docs/claude/overnight-prompt-p1-group1-2026-09-09.md`) shipped CSV export, the booking-widget
style field, and the installable PWA. This prompt builds the two items that group left for
tonight:

1. **Studio structured hours field** (backlog item 18 / report §A1) — a recurring weekly-hours
   entity for the studio, surfaced on the public profile (with JSON-LD), used as a hard gate on
   bookable slots, and editable by the owner.
2. **Timezone handling** (backlog item 16 / report §F14) — a `Studio.Timezone` field and a
   display-layer conversion sweep so appointment times render in the studio's local time instead
   of raw UTC, everywhere a human reads one.

**Both items were re-verified against live source while writing this prompt**, including
several details the backlog spec didn't have (exact extension methods to hook into for the
hours gate, the exact email-template lines currently rendering raw UTC, the runtime's actual
IANA-timezone-data availability). Where source disagreed with or went beyond the backlog spec's
prose, this prompt follows source and says so inline.

The two phases are independent (no shared files, no ordering dependency) — build in either
order. If one phase hits an unresolvable blocker, skip it, note why in the final deliverable,
and continue to the other.

---

## Required reading

```
CLAUDE.md                       — all 7 non-negotiable rules, especially #6/#7
docs/claude/architecture.md     — Feature Module Map; AllowAnonymous Exceptions table (Phase 1
                                   extends an existing anonymous response, adds no new anonymous
                                   route); the "Get Directions" Decisions Log entry
                                   (2026-08-20) — the most recent precedent for extending
                                   PublicStudioResponse, mirror its shape
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/database.md
docs/claude/conventions.md
```

---

## Constraints (identical to every prior overnight prompt in this repo)

- No new npm or NuGet packages. Confirmed while writing this prompt: Phase 2's timezone
  conversion needs no new package on either side — .NET on Linux resolves IANA zone names
  natively via `TimeZoneInfo.FindSystemTimeZoneById` (the runtime image,
  `mcr.microsoft.com/dotnet/aspnet:10.0`, is Debian-based with `tzdata` preinstalled — not the
  `-alpine` variant, which would need it added explicitly; verify this assumption still holds
  with the smoke test in 2-F before relying on it), and the frontend converts with the browser's
  native `Intl.DateTimeFormat`/`toLocaleString(..., { timeZone })`, not a date library.
- No `useEffect` for data fetching. Approved exceptions as documented in every prior prompt.
- TypeScript strict mode, no `any`. Explicit C# types, no unclear `var`.
- No business logic in endpoints — MediatR only. Every command/query with request parameters has
  a FluentValidation validator.
- Tenant isolation via EF Core global query filters, **except** the two already-approved
  `IgnoreQueryFilters()` exceptions Phase 1 touches (see 1-E) — both pre-existing, documented in
  `ArtistAvailabilityExtensions.cs`'s own doc comments, not new exceptions this prompt invents.
- Every endpoint has `.RequireAuthorization()` with the correct policy, except the one Phase 1
  field addition to the already-anonymous `GetPublicStudioQuery` response.
- Never log PII. Structured logs only. No secrets in source.
- Every backend change ships with unit + integration tests. Every frontend change ships with
  component tests covering loading/error/empty states at minimum.
- CLAUDE.md rule #7 (Help sync) applies to both phases — stated explicitly below for each.
- **Do not build blind.** If you discover an open product/business question in either phase that
  isn't already resolved below, stop building that specific sub-item, note it explicitly in the
  final deliverable, and move on — don't guess.

---

# PHASE 1 — Studio Structured Hours

Backlog item 18 / report §A1.

## Design decisions (pre-resolved — do not re-litigate)

- **Entity mirrors `ArtistSchedule` exactly, one field renamed for clarity.** `ArtistSchedule`
  (`Pena_e_Arte.Domain/Entities/ArtistSchedule.cs`) is `TenantEntity` + `DayOfWeek` +
  `TimeSpan StartTime` + `TimeSpan EndTime` + `bool IsAvailable`. `StudioHours` uses the identical
  shape with `IsAvailable` renamed to `IsOpen` — "available" reads naturally for a person
  (an artist), "open" for a place (a studio); this is a naming choice only, the semantics and
  every validation rule are identical.
- **Hard gate, not advisory** — matches the backlog spec's own default call, and matches how
  `StudioClosure` already behaves (a closure unconditionally blocks booking; hours should too,
  for consistency — two different "is the studio bookable right now" checks with different
  strictness would be a confusing, undocumented inconsistency).
- **The gate lives in `ArtistAvailabilityExtensions.cs`, immediately after the existing
  `StudioClosures` check, in both places that check already appears.** This file
  (`Pena_e_Arte.Application/Common/ArtistAvailabilityExtensions.cs`) is the single, deliberately
  shared choke point for all slot-availability logic — its own doc comments explain exactly why
  (shared by authenticated and anonymous/guest callers alike, via
  `IsAnyArtistAvailableAsync`/`CheckArtistScheduleAsync`/`CheckArtistSlotAvailabilityAsync`). Read
  the whole file before touching it. Studio hours is exactly the same class of gate as
  `StudioClosures` (studio-wide, day-based, applies regardless of which specific artist), so it
  belongs at the same two call sites, checked in the same order (closure first, since a closure
  is the coarser, cheaper check — no studio-hours row lookup is worth doing for a day the whole
  studio isn't open anyway), using the exact same `IgnoreQueryFilters()` + explicit-`studioId`
  pattern the file already uses and explains. Do not add a third code path.
- **A studio with zero `StudioHours` rows for a given day of week is closed that day** — same
  "no entry = unavailable" convention `ArtistSchedule` already uses (an artist without a
  Monday-entry is unavailable Mondays; a studio without one behaves the same way). This also
  means a **brand-new studio with no hours configured yet is unbookable everywhere** — flagged
  explicitly since it's a real behavior change: confirm this against the actual registration flow
  (does `RegisterStudioHandler` seed a sane default set of hours, or does the owner have to set
  them before their first booking can succeed?). **Pre-resolved:** seed a default Monday–Friday,
  09:00–18:e, `IsOpen = true` set of `StudioHours` rows in `RegisterStudioHandler` at
  registration (Saturday/Sunday `IsOpen = false` or simply absent — absent is equivalent per the
  "no entry = closed" rule above, so don't bother inserting closed-day rows) — a new studio should
  be bookable immediately with a reasonable default, correctable in settings, not silently
  unbookable until the owner discovers this new required step. This is the same posture
  `SetupChecklist.tsx` already takes toward "things a new owner should set but isn't blocked
  without" — read that file before writing the seed logic, and add "Set your hours" as a new
  checklist item there (see Help sync below) precisely because the seeded default exists to be
  *corrected*, not left as an invisible assumption.

## Backend

### 1-A: `StudioHours.cs` — new entity

```csharp
namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// One entry per day-of-week the studio is open. Mirrors ArtistSchedule's shape exactly
/// (IsAvailable renamed to IsOpen — same semantics, studio- not artist-scoped). No entry
/// for a given DayOfWeek means the studio is closed that day, same "absence = closed"
/// convention ArtistSchedule already uses.
/// </summary>
public class StudioHours : TenantEntity
{
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public bool IsOpen { get; set; } = true;
}
```

Add the corresponding `DbSet<StudioHours> StudioHours` to `IAppDbContext`/`AppDbContext`
(check both — `IAppDbContext` is the interface `ArtistAvailabilityExtensions.cs` and every
handler in this prompt depend on, `AppDbContext` is the EF Core implementation; both need the
new `DbSet`).

### 1-B: Migration

`dotnet ef migrations add AddStudioHours --project Pena_e_Arte.Infrastructure`. Strip the BOM
before committing (standard step, per every prior migration in this repo).

### 1-C: `UpsertStudioHoursCommand` + `GetStudioHoursQuery` — new files

Mirror `UpsertArtistScheduleCommand.cs`/`GetArtistScheduleQuery.cs` file-for-file — same
`Entries`-upsert-by-day loop, same validator shape (at most 7 entries, distinct days per entry,
`StartTime < EndTime`). One behavioral difference: `ArtistSchedule`'s validator doesn't
conditionally skip the `StartTime < EndTime` check for unavailable days (an artist entry always
has real start/end times even when `IsAvailable = false`, per its existing shape) — match that
same convention for `StudioHours`, don't special-case `IsOpen = false` rows in the validator.

New files:
- `Pena_e_Arte.Application/Studios/Commands/UpsertStudioHoursCommand.cs`
  (`record StudioHoursEntryDto(DayOfWeek DayOfWeek, TimeSpan StartTime, TimeSpan EndTime, bool IsOpen)`,
  `record UpsertStudioHoursCommand(IReadOnlyList<StudioHoursEntryDto> Entries) : IRequest` —
  no `StudioId` parameter needed unlike the artist version, since this always targets the
  caller's own studio via `ICurrentTenant`, there's no cross-studio owner action here).
- `Pena_e_Arte.Application/Studios/Queries/GetStudioHoursQuery.cs`
  (`record StudioHoursEntryResponse(DayOfWeek DayOfWeek, TimeSpan StartTime, TimeSpan EndTime, bool IsOpen)`,
  `record GetStudioHoursQuery(Guid StudioId) : IRequest<IReadOnlyList<StudioHoursEntryResponse>>`
  — takes an explicit `StudioId` because, like `GetClosures`, it's read from a
  `{id:guid}`-parameterized route, not always the caller's own studio — check `GetClosures`'s
  actual handler to confirm whether it enforces "id must equal the caller's own studio" for
  non-admin roles or relies on the tenant query filter alone, and match whichever it does.)

### 1-D: Endpoints — `StudioEndpoints.cs`

Mirror the existing `{id:guid}/closures` block exactly (same file, right after it):

```csharp
group.MapGet("{id:guid}/hours", GetHours).RequireAuthorization("ClientAndAbove");
group.MapPut("{id:guid}/hours", UpsertHours).RequireAuthorization("OwnerOnly");
```

Handler bodies mirror `GetClosures`/`AddClosure`'s exact shape (read those two handlers in this
same file immediately before writing these).

### 1-E: The hard gate — `ArtistAvailabilityExtensions.cs`

In **both** `IsAnyArtistAvailableAsync` and `CheckArtistScheduleAsync`, immediately after the
existing `studioClosed` check/early-return, add:

```csharp
StudioHours? hours = await db.StudioHours.IgnoreQueryFilters().FirstOrDefaultAsync(
    h => h.StudioId == studioId && h.DeletedAt == null && h.DayOfWeek == day, ct);

if (hours is null || !hours.IsOpen)
    return false; // IsAnyArtistAvailableAsync
    // or: return (false, "Studio is closed that day."); // CheckArtistScheduleAsync

if (startTime < hours.StartTime || endTime > hours.EndTime)
    return false; // IsAnyArtistAvailableAsync
    // or: return (false, $"Outside studio hours ({hours.StartTime:hh\\:mm}–{hours.EndTime:hh\\:mm})."); // CheckArtistScheduleAsync
```

(`day`/`startTime`/`endTime` are already computed at the top of both methods for the existing
`studioClosed` check — reuse those locals, don't recompute.) Update both methods' doc comments to
mention the new check in their existing "studio-closure → schedule → ..." chain descriptions
(`CheckArtistScheduleAsync`'s comment literally enumerates the chain order — add "studio hours"
to it in the right position, right after "studio-closure").

**This changes `CheckArtistSlotAvailabilityAsync`'s and `IsAnyArtistAvailableAsync`'s behavior
for every existing caller** (`CreateAppointmentCommand`, `RescheduleAppointmentCommand`,
`CheckSlotAvailabilityQuery`, `CheckPublicSlotAvailabilityQuery`, guest booking) — this is the
intended, correct effect of a hard gate, but it means **every appointment test that currently
books a slot without first ensuring `StudioHours` rows exist for that studio/day will start
failing** once this ships, unless 1-C's registration-time seeding (design decisions above) also
extends to test fixtures. Check whatever shared test-fixture/builder creates a `Studio` for
integration tests (grep for a `StudioBuilder`/`TestDataFactory`-shaped helper) and add the same
default weekday hours there — do this as part of 1-E, not as a reactive fix after the test suite
goes red.

## Frontend

### 1-F: `StudioHoursCard.tsx` — new component

Mirror `StudioClosuresCard.tsx`'s file shape (its own RTK Query slice calls, loading/error
states, form pattern) — read that file in full first. Render all 7 days with a time-range
picker per day and an "Open"/"Closed" toggle; a closed day disables (not hides) its time
inputs, matching whatever `ArtistSchedule`'s own editor UI already does for `IsAvailable` (find
that component — likely near `ArtistDetailPage.tsx`'s schedule section — and mirror its exact
toggle/disable interaction pattern rather than inventing a new one).

Add `<StudioHoursCard />` to `StudioProfilePage.tsx` immediately **before**
`<StudioClosuresCard />` (line ~394 as of this writing) — weekly hours are the foundational
setting; closures are exceptions layered on top of them, so that's the more natural reading
order.

### 1-G: RTK Query slice

Add `getStudioHours`/`upsertStudioHours` to whichever existing `studiosApi.ts` slice
`StudioClosuresCard.tsx` already uses — same tag-invalidation pattern its closures
query/mutation pair already follows.

### 1-H: Public profile display + JSON-LD

`StudioPortfolioPage.tsx`:
- Add an hours-of-operation display block (e.g., near the existing address/phone display —
  check this file's current layout for where contact-type info already renders and place it
  there).
- Extend the existing `TattooParlor` JSON-LD object (read the block starting at this file's
  `"@type": "TattooParlor"` line, ~line 49) with `openingHoursSpecification`, schema.org's
  documented shape, one entry per **open** day only (closed days are simply omitted, they're not
  represented with an empty-hours entry):

```typescript
...(hours.some((h) => h.isOpen)
  ? {
      openingHoursSpecification: hours
        .filter((h) => h.isOpen)
        .map((h) => ({
          "@type": "OpeningHoursSpecification",
          dayOfWeek: DAY_NAMES[h.dayOfWeek], // schema.org wants "Monday", not a 0-6 index
          opens: h.startTime,  // "HH:mm" — confirm the API response already serializes
          closes: h.endTime,   //   TimeSpan as "HH:mm:ss" or similar; slice/reformat if not
        }))
    }
  : {}),
```

`GetPublicStudioQuery`/`PublicStudioResponse` needs `Hours` added to reach this component —
extend `PublicStudioResponse` with `IReadOnlyList<PublicStudioHoursResponse> Hours` (new record,
same 4 fields as `StudioHoursEntryResponse` in 1-C — a separate `Contracts` record because
`PublicStudioResponse`'s existing fields all live under `Pena_e_Arte.Contracts.Responses.Public`,
distinct from the owner-facing response namespace, matching this file's existing convention),
and add the corresponding query + mapping in `GetPublicStudioHandler` (mirror exactly how
`Latitude`/`Longitude` were added there on 2026-08-20 — same handler, same "insert one more
field, one more query, one more line in the final `PublicStudioResponse` constructor call"
shape).

## Tests

- Backend: validator tests (entry-count/distinct-day/`StartTime < EndTime` rules — copy
  `UpsertArtistScheduleValidator`'s existing tests and adapt). Handler tests for upsert (add,
  update-in-place) and get. **`ArtistAvailabilityExtensions` tests are the important ones** —
  extend its existing test suite (find it — likely
  `tests/Pena_e_Arte.UnitTests/Common/ArtistAvailabilityExtensionsTests.cs` or similar) with
  cases: no `StudioHours` row for the day → unavailable; `IsOpen = false` → unavailable; slot
  outside `StartTime`/`EndTime` → unavailable with the new reason string; slot inside hours with
  everything else clear → available. Confirm the existing `StudioClosures`-only test cases still
  pass unmodified (closure still wins/short-circuits before hours is even checked).
- Integration test on the new `{id:guid}/hours` endpoints: `OwnerOnly` on the `PUT`, whatever
  `GetClosures` requires on the `GET` (see 1-C's note on confirming that policy's exact
  ownership behavior).
- Frontend: `StudioHoursCard` component test (loading/error/empty/save). `StudioPortfolioPage`
  test — assert `openingHoursSpecification` appears in the rendered JSON-LD `<script>` tag with
  the right shape, and is entirely absent when every day is closed (a studio that hasn't set
  hours yet — though per 1-E's seeding decision this should be rare in practice, it must still
  degrade cleanly).

## Help sync (rule #7)

- `helpContent.ts` — new `owner-studio-hours` entry (or, if the existing studio-profile article
  already covers "things you configure on your profile" broadly, extend it instead of forking a
  new one — check `helpContent.ts` for the current studio-profile-settings entry first, matching
  the judgment call CLAUDE.md rule 7 asks for).
- Standalone manual — same section, one addition.
- `frontend/src/features/help/tours/ownerTour.ts` — add a "Set your hours" step to the
  setup-sequence portion of this tour, pointing at the new `StudioHoursCard`. Per the backlog
  spec's own framing, this pairs naturally with `SetupChecklist.tsx` — **also add "Set your
  hours" as a new item in `SetupChecklist.tsx`** (read that file's existing item shape first,
  match it exactly; this is the same checklist component fixed in PR #118 for a false-incomplete
  flashing bug — don't reintroduce a similar bug by getting the "is this item actually complete"
  check wrong, e.g. checking for the *existence* of any `StudioHours` rows, which 1-E's seeding
  means every studio will have immediately — the checklist item should probably track something
  more meaningful like "has the owner ever saved the hours form," if this repo has a precedent
  for that distinction elsewhere in `SetupChecklist.tsx`; if it doesn't, matching the simpler
  "rows exist" check is an acceptable, explicitly-noted simplification, not a silent gap).

---

# PHASE 2 — Timezone Handling

Backlog item 16 / report §F14.

## Design decisions (pre-resolved — do not re-litigate)

- **Backend storage audit already done while scoping this prompt — comes back cleaner than the
  backlog spec implied.** The backlog spec said "the convention exists in at least one query but
  isn't demonstrably enforced entity-wide" and asked for a grep-every-`DateTime.Now` sweep before
  touching the display layer. That sweep was run across the entire backend
  (`Pena_e_Arte.Domain`, `.Application`, `.Infrastructure`, `.API`, `.Contracts`) as part of
  writing this prompt: **zero** literal `DateTime.Now` call sites exist anywhere. This doesn't
  prove every `DateTime` is correctly `Kind = Utc` on read from MySQL (Pomelo's own UTC handling
  and any `DateTimeKind.Unspecified` coercion on deserialization is a deeper check this prompt
  doesn't re-run), but the specific, cheap check the backlog spec asked for as step one is
  already clean — don't spend time re-deriving that, spend it on the actual remaining
  work below.
- **No new package for timezone lookup, on either side.** .NET on Linux resolves IANA zone
  names (`"Europe/Tirane"`, etc.) natively through `TimeZoneInfo.FindSystemTimeZoneById` — no
  `TimeZoneConverter`/`NodaTime` needed. Confirmed: `Pena_e_Arte.API/Dockerfile`'s runtime stage
  is `mcr.microsoft.com/dotnet/aspnet:10.0`, the Debian-based image (not `-alpine`), which ships
  `tzdata` preinstalled — smoke-test this assumption once (2-F) rather than trusting it blindly,
  since an image change to an Alpine base in the future would silently break this. The frontend
  uses the browser's native `Intl.DateTimeFormat`/`Date.prototype.toLocaleString` with an
  explicit `timeZone` option — no date library needed there either.
- **No coordinate→timezone lookup — fixed fallback default instead, exactly the backlog spec's
  own documented contingency.** The spec's design said "default based on Latitude/Longitude if a
  reasonable IANA-lookup library is already available, else default to a sane fallback and let
  the owner correct it in settings." No such library exists in this codebase and none is being
  added (previous bullet). Use the fallback branch: every new studio defaults to
  **`"Europe/Tirane"`** — this platform's primary/only currently-supported market (matches the
  existing `Payment.Currency` default of `"ALL"`, the NIPT-based studio-identity field, and the
  Stripe-Albania business-registration constraint already on record in `architecture.md`'s
  Decisions Log) — correctable per-studio in settings.
- **Validate `Timezone` input the same way `TimeZoneInfo` will consume it** —
  `TimeZoneInfo.TryFindSystemTimeZoneById(value, out _)` in the validator, not a hand-maintained
  allowlist of IANA names (which would drift from what the runtime actually recognizes and is
  exactly the kind of duplicated-constant risk this codebase already flags elsewhere, e.g. the
  `TattooStyle`/`PortfolioFeed.tsx` duplication fixed in the prior overnight prompt).
- **Convert at the display/render and notification-content boundary only — never in
  business logic, storage, or query predicates.** Every `DateTime` stays UTC in the database and
  in every backend comparison/calculation; the studio's `Timezone` is looked up once per
  rendering/notification call and used purely to format a string for a human to read. This
  matches the backlog spec's own explicit framing and is the entire point of the exercise — get
  this backwards (e.g., converting before a slot-availability comparison) and every existing
  booking-conflict check silently breaks.

## Backend

### 2-A: `Studio.cs`

Add, near the existing `City`/geo fields:

```csharp
/// <summary>IANA/Olson timezone identifier (e.g. "Europe/Tirane"). Used only to format
/// UTC timestamps for display/notifications — never for storage or business-logic
/// comparisons, which stay UTC throughout. Defaults to the platform's primary market at
/// registration; owner-correctable in studio settings.</summary>
public string Timezone { get; set; } = "Europe/Tirane";
```

### 2-B: Migration

`dotnet ef migrations add AddStudioTimezone --project Pena_e_Arte.Infrastructure`. Give the
column a non-null default (`HasDefaultValue("Europe/Tirane")` in
`StudioConfiguration.cs`, mirroring `StorageUsageBytes`'s existing `HasDefaultValue(0L)` line in
the same file) so the migration backfills every existing studio row, not just new ones — strip
the BOM before committing.

### 2-C: `RegisterStudioHandler.cs`

The entity's own default value (2-A) already covers this — `new Studio { ... }` without an
explicit `Timezone` assignment picks up `"Europe/Tirane"` automatically via the C# property
initializer. Confirm this is actually true for EF Core's object-initializer + `Add()` flow in
this codebase (it should be, since nothing here overrides the CLR default before `SaveChangesAsync`),
rather than assuming — if for any reason it isn't, set `Timezone = "Europe/Tirane"` explicitly in
the `Studio` object initializer, same line as the existing `Latitude`/`Longitude` assignments.

### 2-D: Owner-editable — `UpdateMyStudioCommand`/`UpdateMyStudioRequest`

Add `Timezone` as an optional field, following whatever pattern this command's existing optional
string fields (`Description`, `PhoneNumber`, etc.) already use — read `UpdateMyStudioCommand.cs`
and its validator in full before editing. Validator rule:

```csharp
RuleFor(x => x.Request.Timezone)
    .Must(tz => tz is null || TimeZoneInfo.TryFindSystemTimeZoneById(tz, out _))
    .WithMessage("Timezone must be a valid IANA timezone identifier (e.g. 'Europe/Tirane').");
```

### 2-E: `GetMyStudioQuery`/`StudioResponse` and `PublicStudioResponse`

Add `Timezone` to whichever response(s) the frontend needs it from — at minimum
`StudioResponse` (owner-authenticated, for the settings form and for 2-G's frontend display
work). Decide whether `PublicStudioResponse` also needs it (a guest viewing the public profile
doesn't strictly need the raw IANA string, but Phase 1's `openingHoursSpecification` JSON-LD
technically ought to be timezone-qualified per schema.org's stricter interpretations — this is a
minor SEO nicety, not a functional requirement; add it to `PublicStudioResponse` only if it's a
one-line addition alongside 1-H's other changes to that same response, don't make it its own
separate round-trip).

### 2-F: Notification content — `EmailRenderer.cs`

Four call sites currently format the raw UTC `appointment.Date` directly
(`Pena_e_Arte.Infrastructure/Services/MailKit/EmailRenderer.cs`, lines ~60, ~81, ~99, ~117, all
`date.ToString("dddd, dd MMMM yyyy 'at' HH:mm", ...)`). Read the full file first — in particular,
check what each of these four methods currently receives as parameters (a raw `DateTime`, or an
object that also carries the `Studio`/`StudioId`) to know whether `Timezone` is already reachable
at each call site or needs threading through from the caller.

Add a small conversion helper (either a static method in this file, or a new
`Pena_e_Arte.Application/Common/TimezoneUtils.cs` if other backend code will also need it —
Phase 1 doesn't, so a local private helper in `EmailRenderer.cs` is sufficient scope for now):

```csharp
private static DateTime ToStudioLocal(DateTime utc, string timezone)
{
    try
    {
        TimeZoneInfo tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
    }
    catch (TimeZoneNotFoundException)
    {
        // Defensive — should be unreachable given 2-D's validator, but a template render
        // must never throw and lose a notification over a bad timezone string.
        return utc;
    }
}
```

Apply it at all four call sites: `["appointment_date"] = ToStudioLocal(date, studio.Timezone).ToString("dddd, dd MMMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture)`.
Confirm `NotificationService.cs` (the SMS dispatch path) doesn't independently format
`appointment_date` a second time from a raw `DateTime` — if it reuses `EmailRenderer`'s already-
rendered string (likely, given the shared `["appointment_date"]` placeholder key), this fix
covers SMS for free; if it has its own separate formatting call, apply the same fix there.

**Smoke test (per this phase's "no new package" constraint above):** add a one-line integration
test asserting `TimeZoneInfo.FindSystemTimeZoneById("Europe/Tirane")` doesn't throw in this
project's actual test-run environment (not just locally) — this is the cheapest possible guard
against the Alpine-base-image risk named above, and should run in CI on every future change, not
just tonight.

## Frontend

### 2-G: Thread `Timezone` from the current studio into the display layer

Confirm how these six files currently obtain "the current studio" (an existing
`useGetMyStudioQuery()` call, a Redux slice populated at login/`useEnsureActiveStudio.ts`, or
per-appointment data that doesn't carry studio context at all) before deciding the cleanest way
to reach `timezone` from each — this wasn't fully traced while writing this prompt, and the
right approach differs depending on what's already there:

- `frontend/src/features/appointments/components/AppointmentCard.tsx`
- `frontend/src/features/appointments/components/AppointmentDetailPage.tsx`
- `frontend/src/features/appointments/components/MyBookingsSection.tsx`
- `frontend/src/features/appointments/components/SchedulePage.tsx`
- `frontend/src/features/reports/components/MyEarningsPage.tsx`
- `frontend/src/features/reports/components/RevenueTrendChart.tsx`

For each, replace the existing `.toLocaleString()`/`.toLocaleDateString()`/`.toLocaleTimeString()`
call with the same call plus an explicit `{ timeZone: studioTimezone }` option (the browser's
native `Intl` machinery accepts an IANA string identically to the backend's `TimeZoneInfo`) —
e.g. `date.toLocaleString("en-GB", { timeZone: studioTimezone, ...existingOptions })`. Do not
change any other formatting option already present (locale, `dateStyle`, etc.) — this is purely
adding the missing timezone qualifier to calls that currently implicitly use the *browser's*
local timezone, which is wrong for exactly the cross-timezone-booking scenario (a
guest-artist/tattoo-tourism client viewing their own upcoming appointment from a different
timezone than the studio) this item exists to fix.

**`MyBookingsSection.tsx` is the highest-value and highest-risk of the six** — it's the page a
client checks right before an appointment, across potentially any timezone, and (per the prior
overnight prompt's own "Out of Scope" note on the Google Maps deep-link work) doesn't currently
carry studio context at all beyond an id. Confirm what studio data is actually available to this
component today before assuming `studioTimezone` is a trivial prop-thread — if it isn't
reachable without a larger data-plumbing change, that's a real, separately-scoped finding to
surface in the final deliverable, not something to force through tonight with a wrong or
hardcoded value.

Add a small shared helper if the same `{ timeZone }`-injection pattern repeats identically across
all six files — `frontend/src/shared/utils/formatInStudioTimezone.ts` (mirrors this repo's
existing `formatRelativeTime.ts` as a small, single-purpose date-utility precedent) — rather than
duplicating the option object six times.

## Tests

- Backend: validator test (`TimeZoneInfo.TryFindSystemTimeZoneById` rejects garbage, accepts
  `"Europe/Tirane"`/`"America/New_York"`/etc.). `EmailRenderer` test — render with a non-UTC
  studio timezone (e.g. `"America/New_York"`, deliberately far from UTC to make a bug obvious in
  a failing assertion) and assert the rendered `appointment_date` string reflects the converted
  local time, not the raw UTC value. The 2-F smoke test.
- Frontend: for each of the six files touched, extend its existing test (or add one if none
  covers this rendering path) asserting a UTC timestamp renders using the passed-in
  `studioTimezone`, not the test environment's local timezone (set `studioTimezone` to something
  distinctly different from UTC in the test, same "make a bug obvious" principle as the backend
  test above).

## Help sync (rule #7)

Per the backlog spec's own call, and confirmed: no dedicated new help entry — this is a
correctness fix, not a new user-facing feature, beyond the one new settings field. Add a
one-line mention of the new "Timezone" field to whichever existing help entry documents the
studio-profile settings form (same entry 1-I checks/extends for studio hours — if both phases
land in the same session, this is one edit to one entry covering both new fields, not two
separate edits). No manual or tour changes needed beyond that same one-line mention in the
manual's matching section.

---

## Out of Scope — flagged explicitly, not silently dropped

1. **Deeper UTC-storage audit beyond the `DateTime.Now` grep** (`DateTimeKind` coercion on reads
   from MySQL/Pomelo, any raw SQL or migration-authored `DateTime` literals) — the cheap check
   came back clean (see Phase 2's design decisions), but a full audit of read-path `Kind`
   handling wasn't performed and would be its own, more invasive verification pass.
2. **`MyBookingsSection.tsx`'s studio-context gap** (flagged in 2-G) — if it turns out this page
   genuinely can't reach `studioTimezone` without a larger data-plumbing change, that's a real
   follow-up, not a silent downgrade to "show UTC" or a hardcoded guess.
3. **A studio's `Timezone` vs. an individual artist's or client's own timezone** — this phase
   converts every display to the *studio's* local time uniformly (the correct behavior for "when
   is my appointment, studio-local," which is what actually matters for showing up on time) but
   deliberately does not attempt per-viewer timezone detection/display (e.g. "your appointment is
   at 3pm studio time, which is 9am for you") — that's a materially larger feature, not part of
   this correctness fix.

---

# PHASE 3 — Backlog Carry-Forward

Update `docs/claude/p1-backlog-master-build-spec-2026-09-09.md` with a dated addendum:

- Move items 18 and 16 from "Group 2" to "shipped 2026-09-09," each with a one-line pointer to
  the files touched.
- Note that item 18's shipment (specifically, `StudioHours` existing as a real gate) is a
  dependency several Group-3/4 items may eventually want to reference (e.g. any future feature
  that reasons about "when can a client interact with this studio at all") — don't invent a
  connection that isn't real, but note the precedent if a later item's own spec would benefit
  from citing this one.
- Record the three "Out of Scope" items above as newly-identified follow-up candidates.

---

## Final self-check before declaring done

```bash
dotnet build      # 0 errors, 0 warnings
dotnet test       # all green, including every new/extended test above
cd frontend
pnpm tsc --noEmit
pnpm lint
pnpm test --run   # all green, including every new/extended test above
pnpm build
```

Plus, walk through each explicitly:

- The full existing appointment/booking test suite still passes after 1-E's hard-gate change —
  if any existing test now fails because its fixture studio has no `StudioHours` rows, that's
  expected and must be fixed at the fixture level (per 1-E's own note), not worked around by
  loosening the gate.
- `GET {id:guid}/hours` and `PUT {id:guid}/hours` have the correct policies, verified by an
  actual non-owner-token integration test for the `PUT`.
- The `openingHoursSpecification` JSON-LD block was verified to actually render correct,
  schema.org-valid output for a studio with a realistic mixed open/closed weekly schedule — not
  just for the all-open or all-closed edge cases.
- `TimeZoneInfo.FindSystemTimeZoneById("Europe/Tirane")` was confirmed not to throw in this
  project's real test/build environment (2-F's smoke test actually ran and passed, not just
  written).
- Every one of the six frontend display-layer files in 2-G was actually changed and actually
  tested — this list came from a real grep, not from memory, so treat it as a checklist to
  verify against, and if the grep missed a seventh site (a raw `new Date(...).toString()` or a
  hand-rolled formatter that doesn't go through `.toLocaleString()`), fix that one too and note
  the addition.
- `helpContent.ts`, the standalone manual, `ownerTour.ts`, and `SetupChecklist.tsx` are all
  updated per Phase 1's Help sync section.
- Every design decision stated as "pre-resolved" above was actually followed — if live source
  didn't match an assumption this prompt made (whether `EmailRenderer`'s methods already receive
  `Studio`/`Timezone`, `GetClosures`'s exact ownership-check behavior, how the six frontend files
  currently reach "current studio," or the `RegisterStudioHandler` default-value behavior in
  2-C), that deviation and what you did instead is called out explicitly in the final
  deliverable, not silently absorbed.

---

## Final Deliverable

Append a new section to `docs/claude/architecture.md`'s Decisions Log:

```markdown
| Studio structured hours + timezone handling — P1 Group 2 (2026-09-09) | [what shipped per phase, files touched, any deviation from this prompt's stated assumptions — especially anything found in Phase 2's frontend studio-context tracing (2-G) or the EmailRenderer parameter-threading check (2-F)] | Current vertical-booking-SaaS standard (CLAUDE.md rule 6) — structured hours with JSON-LD opening-hours markup is baseline for local-business discoverability (Fresha/Vagaro/Boulevard-class studio profiles all surface this), and correct local-time display is a correctness baseline, not a feature, for any booking product serving guest-artist/tattoo-tourism traffic across timezones. |
```

Commit:

```
git add -A && git commit -m "feat: studio structured hours (hard-gated) + timezone-aware display — P1 backlog group 2"
```
