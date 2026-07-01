# Overnight Prompt — Book Appointment Form Overhaul
**Goal:** Fix every confirmed bug, visual defect, and UX friction point in the
`BookAppointmentForm` / `BookPage` flow, then add real-time slot availability
checking and artist avatars in the dropdown. The 3-step flow
(form → deposit → confirmation) is already working correctly — preserve it entirely.

No new npm or NuGet packages. All changes must pass `pnpm tsc --noEmit`, `pnpm lint`,
and `pnpm test --run` before the session ends.

---

## Read First

1. `CLAUDE.md`
2. `docs/claude/frontend.md`
3. `docs/claude/backend.md`
4. `docs/claude/database.md`
5. `docs/claude/architecture.md`
6. `docs/claude/conventions.md`

---

## Source Files to Read Before Starting

Read each file in full before making any change:

- `frontend/src/features/appointments/components/BookAppointmentForm.tsx`
- `frontend/src/features/appointments/components/BookPage.tsx`
- `frontend/src/features/appointments/components/MyBookingsSection.tsx`
- `frontend/src/features/appointments/appointment.types.ts`
- `frontend/src/features/appointments/appointmentsApi.ts`
- `frontend/src/features/artists/artistsApi.ts`
- `frontend/src/features/appointments/__tests__/BookPage.test.tsx`
- `frontend/src/shared/components/ui/select.tsx` ← read this to find the icon bug
- `Pena_e_Arte.Application/Appointments/Commands/CreateAppointmentCommand.cs`
- `Pena_e_Arte.Application/Appointments/Queries/GetAppointmentsQuery.cs`
- `Pena_e_Arte.Domain/Entities/Artist.cs`
- `Pena_e_Arte.Contracts/Responses/ArtistResponse.cs`
- `Pena_e_Arte.API/Endpoints/ArtistEndpoints.cs`
- `Pena_e_Arte.API/Endpoints/AppointmentEndpoints.cs`

---

## Files to Change

| File | What changes |
|---|---|
| `Pena_e_Arte.Domain/Entities/Artist.cs` | Add `IsActive`, `AvatarUrl` |
| `Pena_e_Arte.Contracts/Responses/ArtistResponse.cs` | Add `IsActive`, `AvatarUrl` |
| `Pena_e_Arte.Application/Artists/Queries/GetArtistsQuery.cs` | Filter `IsActive` |
| `Pena_e_Arte.Application/Appointments/Queries/CheckSlotAvailabilityQuery.cs` | New |
| `Pena_e_Arte.API/Endpoints/AppointmentEndpoints.cs` | Add slot-check endpoint |
| `frontend/src/shared/components/ui/select.tsx` | Fix SelectTrigger icon |
| `frontend/src/features/artists/artistsApi.ts` | Add `isActive`, `avatarUrl` to `ArtistResponse` |
| `frontend/src/features/appointments/appointmentsApi.ts` | Add `useCheckSlotAvailabilityQuery` |
| `frontend/src/features/appointments/appointment.types.ts` | Add `SlotAvailabilityResponse` |
| `frontend/src/features/appointments/components/BookAppointmentForm.tsx` | Full redesign |
| `frontend/src/features/appointments/components/BookPage.tsx` | Heading, layout, back link |
| `frontend/src/features/appointments/__tests__/BookPage.test.tsx` | Update + add tests |
| `docs/claude/architecture.md` | Update decisions log |

---

## Section 1 — Backend: Artist entity improvements

### 1-A: Add `IsActive` and `AvatarUrl` to `Artist`

Read `Pena_e_Arte.Domain/Entities/Artist.cs`. Add two properties:

```csharp
/// <summary>
/// False for seed/test records that should not appear in client-facing dropdowns.
/// Default true for all new artists.
/// </summary>
public bool IsActive { get; set; } = true;

/// <summary>
/// URL of the artist's profile photo. Used in the booking dropdown.
/// Null when not set — show initials fallback in the UI.
/// </summary>
public string? AvatarUrl { get; set; }
```

Configure in `AppDbContext`:
```csharp
b.Property(a => a.IsActive).HasDefaultValue(true);
b.Property(a => a.AvatarUrl).HasMaxLength(512).IsRequired(false);
```

### 1-B: Update `ArtistResponse` contract

Read `Pena_e_Arte.Contracts/Responses/ArtistResponse.cs`. Add to the record:
```csharp
public record ArtistResponse(
    Guid    Id,
    Guid    StudioId,
    Guid?   UserId,
    string  FirstName,
    string  LastName,
    string  Email,
    string? Specializations,
    decimal? HourlyRate,
    bool    IsActive,        // ← NEW
    string? AvatarUrl,       // ← NEW
    IReadOnlyList<string> PortfolioImages,
    string? Slug,
    DateTime CreatedAt,
    DateTime UpdatedAt);
```

Update all mapping calls in the codebase that construct `ArtistResponse` to pass the
two new fields. Search for `new ArtistResponse(` and fix each one.

### 1-C: Filter inactive artists from `GetArtistsQuery`

Read `Pena_e_Arte.Application/Artists/Queries/GetArtistsQuery.cs`.
In the handler's EF Core query, add `.Where(a => a.IsActive)` so that seed/test
records with `IsActive = false` are never returned to the client-facing dropdown.

The existing optional `search` filter remains — only the `IsActive` filter is added:

```csharp
IQueryable<Artist> query = db.Artists.Where(a => a.IsActive);

if (!string.IsNullOrWhiteSpace(request.Search))
    query = query.Where(a =>
        a.FirstName.Contains(request.Search) ||
        a.LastName.Contains(request.Search));
```

### 1-D: Deduplicate guard in the query

The dropdown showed "Carla Neves" twice. Ensure the query uses `.DistinctBy(a => a.Id)` (or `.GroupBy(a => a.Id).Select(g => g.First())`) as a safety net against any JOIN-related duplicates:

```csharp
return await query
    .OrderBy(a => a.FirstName).ThenBy(a => a.LastName)
    .DistinctBy(a => a.Id)        // ← safety dedup
    .Select(a => new ArtistResponse(...))
    .ToListAsync(ct);
```

### 1-E: Update existing seed data

In `Pena_e_Arte.Infrastructure/Data/Seeder.cs` (or whichever file seeds data):
- Find the "Artist Test" seed entry and set `IsActive = false`.
- Find any artist with a digit in their first or last name (`Sofia1 Alves`) and either:
  - Fix the name to `"Sofia Alves"`, or
  - Set `IsActive = false` if it is a test record.

### 1-F: Migration

```bash
dotnet ef migrations add AddArtistIsActiveAndAvatarUrl \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
dotnet ef database update \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
```

---

## Section 2 — Backend: Slot availability check endpoint (new)

This is the most impactful feature addition: before the user submits the booking
form, the frontend fires a lightweight check so the user knows if the slot is free.

### 2-A: `CheckSlotAvailabilityQuery`

Create `Pena_e_Arte.Application/Appointments/Queries/CheckSlotAvailabilityQuery.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Application.Appointments.Queries;

public record CheckSlotAvailabilityQuery(
    Guid     ArtistId,
    DateTime Date,
    int      DurationMinutes)
    : IRequest<SlotAvailabilityResult>;

public record SlotAvailabilityResult(bool Available, string? Reason);

public class CheckSlotAvailabilityHandler(IAppDbContext db)
    : IRequestHandler<CheckSlotAvailabilityQuery, SlotAvailabilityResult>
{
    public async Task<SlotAvailabilityResult> Handle(
        CheckSlotAvailabilityQuery query, CancellationToken ct)
    {
        DateTime end = query.Date.AddMinutes(query.DurationMinutes);

        // Check artist schedule
        DayOfWeek day         = query.Date.DayOfWeek;
        TimeSpan  startTime   = query.Date.TimeOfDay;
        TimeSpan  endTime     = end.TimeOfDay;

        var schedule = await db.ArtistSchedules
            .Where(s => s.ArtistId == query.ArtistId &&
                        s.DayOfWeek == day &&
                        s.IsAvailable)
            .FirstOrDefaultAsync(ct);

        if (schedule is null)
            return new SlotAvailabilityResult(false,
                $"Artist is not available on {day}s.");

        if (startTime < schedule.StartTime || endTime > schedule.EndTime)
            return new SlotAvailabilityResult(false,
                $"Outside artist's hours ({schedule.StartTime:hh\\:mm}–{schedule.EndTime:hh\\:mm}).");

        // Check time off
        bool onLeave = await db.ArtistTimeOffs.AnyAsync(
            t => t.ArtistId == query.ArtistId &&
                 t.StartDate <= query.Date.Date &&
                 t.EndDate   >= query.Date.Date, ct);

        if (onLeave)
            return new SlotAvailabilityResult(false, "Artist is on leave that day.");

        // Check booking conflicts
        bool conflict = await db.Appointments.AnyAsync(a =>
            a.ArtistId == query.ArtistId &&
            a.Date     < end            &&
            a.EndDate  > query.Date     &&
            a.Status   != AppointmentStatus.Cancelled, ct);

        if (conflict)
            return new SlotAvailabilityResult(false, "That slot is already booked.");

        return new SlotAvailabilityResult(true, null);
    }
}
```

### 2-B: Contract type

Add `SlotAvailabilityResponse` to `Pena_e_Arte.Contracts/Responses/`:

```csharp
namespace Pena_e_Arte.Contracts.Responses;

public record SlotAvailabilityResponse(bool Available, string? Reason);
```

### 2-C: Add `CheckSlotAvailability` validator

Create `Pena_e_Arte.Application/Appointments/Validators/CheckSlotAvailabilityValidator.cs`:

```csharp
using FluentValidation;
using Pena_e_Arte.Application.Appointments.Queries;

namespace Pena_e_Arte.Application.Appointments.Validators;

public class CheckSlotAvailabilityValidator
    : AbstractValidator<CheckSlotAvailabilityQuery>
{
    public CheckSlotAvailabilityValidator()
    {
        RuleFor(x => x.ArtistId).NotEmpty();
        RuleFor(x => x.Date).GreaterThan(DateTime.UtcNow)
            .WithMessage("Date must be in the future.");
        RuleFor(x => x.DurationMinutes).InclusiveBetween(30, 480);
    }
}
```

### 2-D: Register endpoint

Read `Pena_e_Arte.API/Endpoints/AppointmentEndpoints.cs`. Add:

```csharp
// Slot availability pre-check — called by the booking form before submit
group.MapGet("/check-slot", CheckSlotAvailability)
     .RequireAuthorization("ClientAndAbove");

private static async Task<IResult> CheckSlotAvailability(
    Guid     artistId,
    DateTime date,
    int      durationMinutes,
    ISender  mediator,
    CancellationToken ct)
{
    var result = await mediator.Send(
        new CheckSlotAvailabilityQuery(artistId, date, durationMinutes), ct);
    return Results.Ok(new SlotAvailabilityResponse(result.Available, result.Reason));
}
```

---

## Section 3 — Frontend: Fix the SelectTrigger icon bug

### 3-A: Investigate `select.tsx`

Read `frontend/src/shared/components/ui/select.tsx`. Find the `SelectTrigger` component.
It should render `<ChevronDown />` from `lucide-react`. Confirm what icon is actually
being imported and rendered.

**If the issue is a wrong icon import:**
```tsx
// Wrong (whatever is currently there, e.g. MousePointer, ArrowUpRight, PenLine):
import { SomeWrongIcon } from "lucide-react";

// Correct:
import { ChevronDown } from "lucide-react";
```

**If the icon is correct but the CSS is wrong (e.g., `rotate-45` applied):**
Remove any rotation transform that isn't the toggled open/closed state.

**Correct implementation of the trigger with animated chevron:**
```tsx
const SelectTrigger = React.forwardRef<...>(
  ({ className, children, ...props }, ref) => (
    <RadixSelect.Trigger
      ref={ref}
      className={cn(
        "flex h-9 w-full items-center justify-between whitespace-nowrap rounded-md border",
        "border-input bg-transparent px-3 py-2 text-sm shadow-sm",
        "placeholder:text-muted-foreground",
        "focus:outline-none focus:ring-1 focus:ring-ring",
        "disabled:cursor-not-allowed disabled:opacity-50",
        "[&>span]:line-clamp-1",
        className,
      )}
      {...props}
    >
      {children}
      <RadixSelect.Icon asChild>
        {/* ChevronDown — rotates to ChevronUp when open via data-state="open" */}
        <ChevronDown
          className="h-4 w-4 opacity-50 shrink-0
                     transition-transform duration-200
                     [[data-state=open]_&]:rotate-180"
        />
      </RadixSelect.Icon>
    </RadixSelect.Trigger>
  ),
);
```

The `[[data-state=open]_&]:rotate-180` Tailwind variant rotates the chevron 180°
(pointing up) when the `SelectTrigger` has `data-state="open"` — Radix sets this
automatically.

---

## Section 4 — Frontend: Add slot availability to `appointmentsApi.ts`

Read `frontend/src/features/appointments/appointmentsApi.ts`. Add:

### 4-A: `SlotAvailabilityResponse` type in `appointment.types.ts`

```typescript
export interface SlotAvailabilityResponse {
  available: boolean;
  reason:    string | null;
}

export interface CheckSlotAvailabilityParams {
  artistId:        string;
  date:            string;   // ISO 8601 string, e.g. "2026-07-10T14:00"
  durationMinutes: number;
}
```

### 4-B: RTK Query endpoint

Add to `appointmentsApi`:

```typescript
checkSlotAvailability: builder.query<SlotAvailabilityResponse, CheckSlotAvailabilityParams>({
  query: ({ artistId, date, durationMinutes }) => ({
    url: "appointments/check-slot",
    params: { artistId, date, durationMinutes },
  }),
  // No cache tag — always re-check (slots change in real time)
  keepUnusedDataFor: 0,
}),
```

Export `useCheckSlotAvailabilityQuery`.

---

## Section 5 — Frontend: `ArtistResponse` — add `isActive` and `avatarUrl`

Read `frontend/src/features/artists/artistsApi.ts`. Add to `ArtistResponse`:

```typescript
export interface ArtistResponse {
  id:              string;
  studioId:        string;
  userId:          string | null;
  firstName:       string;
  lastName:        string;
  email:           string;
  specializations: string | null;
  hourlyRate:      number | null;
  isActive:        boolean;       // ← NEW
  avatarUrl:       string | null; // ← NEW
  portfolioImages: string[];
  slug:            string | null;
  createdAt:       string;
  updatedAt:       string;
}
```

---

## Section 6 — Frontend: `BookAppointmentForm.tsx` — complete redesign

Read the current file in full. Rewrite it with all the improvements below.
The multi-step state machine (`booked`, `depositDone`) and the deposit/confirmation
screens (Steps 2 and 3) are **correct and must remain unchanged**. Only the form step
(Step 1) changes.

### 6-A: Required-field label helper

Add a small helper component at the top of the file (above the form component):

```tsx
function FieldLabel({
  htmlFor,
  required = false,
  children,
}: {
  htmlFor:  string;
  required?: boolean;
  children: React.ReactNode;
}) {
  return (
    <Label htmlFor={htmlFor} className="text-xs font-medium text-muted-foreground">
      {children}
      {required && (
        <span aria-hidden="true" className="ml-0.5 text-destructive">*</span>
      )}
    </Label>
  );
}
```

Use `<FieldLabel htmlFor="artistId" required>Artist</FieldLabel>` on every required
field, and `<FieldLabel htmlFor="notes">Notes</FieldLabel>` on optional ones.

Add a `<p className="text-xs text-muted-foreground/60">* Required</p>` legend at the
top of the form.

### 6-B: Artist selector — fix icon, add avatar, add search

The artist dropdown currently renders plain text. Replace the `<SelectItem>` content
with an avatar + name. For avatars, use the `avatarUrl` if available, or render an
initials circle.

**Artist avatar sub-component (inside the file):**

```tsx
function ArtistAvatar({ artist }: { artist: ArtistResponse }) {
  const initials = `${artist.firstName[0] ?? ""}${artist.lastName[0] ?? ""}`.toUpperCase();

  if (artist.avatarUrl) {
    return (
      <img
        src={artist.avatarUrl}
        alt=""
        aria-hidden="true"
        className="h-6 w-6 rounded-full object-cover shrink-0"
      />
    );
  }

  return (
    <span
      aria-hidden="true"
      className="h-6 w-6 rounded-full bg-violet-600/20 text-violet-400
                 text-[9px] font-semibold flex items-center justify-center shrink-0"
    >
      {initials}
    </span>
  );
}
```

**Artist selector with search:**

```tsx
{/* Artist selector */}
<div className="space-y-1.5">
  <FieldLabel htmlFor="artistId" required>Artist</FieldLabel>
  <Controller
    control={control}
    name="artistId"
    render={({ field }) => (
      <Select
        disabled={loadingArtists}
        value={field.value}
        onValueChange={field.onChange}
      >
        <SelectTrigger
          id="artistId"
          aria-label="Select artist"
          className={cn(errors.artistId && "border-destructive")}
        >
          {/* Show selected artist with avatar in the trigger */}
          {field.value && selectedArtist ? (
            <span className="flex items-center gap-2">
              <ArtistAvatar artist={selectedArtist} />
              <span>{selectedArtist.firstName} {selectedArtist.lastName}</span>
            </span>
          ) : (
            <SelectValue placeholder={loadingArtists ? "Loading artists…" : "Choose an artist"} />
          )}
        </SelectTrigger>
        <SelectContent>
          {/* Search input at top of dropdown */}
          <div className="px-2 pb-1.5 pt-1">
            <input
              type="text"
              placeholder="Search artists…"
              value={artistSearch}
              onChange={(e) => setArtistSearch(e.target.value)}
              className="w-full rounded-sm border-0 bg-muted/50 px-2 py-1
                         text-xs placeholder:text-muted-foreground/60
                         focus:outline-none focus:ring-1 focus:ring-ring"
              aria-label="Search artists"
            />
          </div>
          {filteredArtists.length === 0 ? (
            <div className="py-4 text-center text-xs text-muted-foreground">
              {artists?.length === 0
                ? "No artists configured for this studio."
                : "No artists match your search."}
            </div>
          ) : (
            filteredArtists.map((a) => (
              <SelectItem key={a.id} value={a.id}>
                <span className="flex items-center gap-2">
                  <ArtistAvatar artist={a} />
                  <span className="flex flex-col">
                    <span className="text-sm">{a.firstName} {a.lastName}</span>
                    {a.specializations && (
                      <span className="text-[10px] text-muted-foreground truncate max-w-[180px]">
                        {a.specializations}
                      </span>
                    )}
                  </span>
                </span>
              </SelectItem>
            ))
          )}
        </SelectContent>
      </Select>
    )}
  />
  {errors.artistId && (
    <p className="text-xs text-destructive" role="alert">
      {errors.artistId.message}
    </p>
  )}
</div>
```

**State for search and derived values (add at top of component):**

```tsx
const [artistSearch, setArtistSearch] = useState("");

// Deduplicate by id (safety net against any backend duplicate)
const uniqueArtists = useMemo(
  () => {
    const seen = new Set<string>();
    return (artists ?? []).filter((a) => {
      if (seen.has(a.id)) return false;
      seen.add(a.id);
      return true;
    });
  },
  [artists],
);

const filteredArtists = useMemo(
  () => {
    const term = artistSearch.toLowerCase().trim();
    if (!term) return uniqueArtists;
    return uniqueArtists.filter((a) =>
      `${a.firstName} ${a.lastName}`.toLowerCase().includes(term) ||
      (a.specializations ?? "").toLowerCase().includes(term),
    );
  },
  [uniqueArtists, artistSearch],
);

const selectedArtist = useMemo(
  () => uniqueArtists.find((a) => a.id === watchedArtistId) ?? null,
  [uniqueArtists, watchedArtistId],
);
```

Add `watchedArtistId` using `useWatch`:
```tsx
import { useForm, Controller, useWatch } from "react-hook-form";
// ...
const watchedArtistId    = useWatch({ control, name: "artistId" });
const watchedDate        = useWatch({ control, name: "scheduledAt" });
const watchedDuration    = useWatch({ control, name: "durationMinutes" });
```

### 6-C: Duration — replace `<Input type="number">` with `<Select>`

**Problem:** A free-form number spinner accepts invalid durations like 17 minutes.
**Fix:** Replace with a `<Select>` containing studio-standard session lengths.

```tsx
const DURATION_OPTIONS = [
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
] as const;

{/* Duration — grouped with Date & Time (Gestalt proximity) */}
<div className="grid grid-cols-2 gap-3">
  {/* Date & Time */}
  <div className="space-y-1.5 col-span-2 sm:col-span-1">
    <FieldLabel htmlFor="scheduledAt" required>Date &amp; Time</FieldLabel>
    <Input
      id="scheduledAt"
      type="datetime-local"
      min={new Date().toISOString().slice(0, 16)}
      {...register("scheduledAt")}
      className={cn(errors.scheduledAt && "border-destructive")}
    />
    {errors.scheduledAt && (
      <p className="text-xs text-destructive" role="alert">
        {errors.scheduledAt.message}
      </p>
    )}
  </div>

  {/* Session length */}
  <div className="space-y-1.5 col-span-2 sm:col-span-1">
    <FieldLabel htmlFor="durationMinutes" required>Session Length</FieldLabel>
    <Controller
      control={control}
      name="durationMinutes"
      render={({ field }) => (
        <Select
          value={String(field.value)}
          onValueChange={(v) => field.onChange(Number(v))}
        >
          <SelectTrigger
            id="durationMinutes"
            className={cn(errors.durationMinutes && "border-destructive")}
          >
            <SelectValue placeholder="Select duration" />
          </SelectTrigger>
          <SelectContent>
            {DURATION_OPTIONS.map(({ value, label }) => (
              <SelectItem key={value} value={String(value)}>
                {label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      )}
    />
    {errors.durationMinutes && (
      <p className="text-xs text-destructive" role="alert">
        {errors.durationMinutes.message}
      </p>
    )}
  </div>
</div>
```

Update the Zod schema — `durationMinutes` is now always one of the preset values:
```typescript
durationMinutes: z.number().refine(
  (v) => [30, 45, 60, 90, 120, 180, 240, 300, 360, 480].includes(v),
  "Select a valid session length"
),
```

### 6-D: Real-time slot availability check

After the user selects an artist + date + duration, fire a debounced availability check
and show inline feedback below the date/time group.

```tsx
// Debounced slot-check args — only fetch when all three are present
const [debouncedCheck, setDebouncedCheck] =
  useState<CheckSlotAvailabilityParams | null>(null);

useEffect(() => {
  if (!watchedArtistId || !watchedDate || !watchedDuration) {
    setDebouncedCheck(null);
    return;
  }
  // Small debounce so we don't fire on every keystroke in the date field
  const timer = setTimeout(() => {
    setDebouncedCheck({
      artistId:        watchedArtistId,
      date:            watchedDate,
      durationMinutes: watchedDuration,
    });
  }, 600);
  return () => clearTimeout(timer);
}, [watchedArtistId, watchedDate, watchedDuration]);
```

Note: This `useEffect` manages debouncing — it does NOT fetch data. `useCheckSlotAvailabilityQuery` (RTK Query) does the fetch.

```tsx
const {
  data:      slotStatus,
  isFetching: checkingSlot,
} = useCheckSlotAvailabilityQuery(debouncedCheck!, {
  skip: debouncedCheck === null,
});
```

**Availability indicator component:**

```tsx
function SlotAvailabilityIndicator({
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

Add `AlertCircle` to the imports from `lucide-react`.

Render `<SlotAvailabilityIndicator>` directly below the date/duration group:
```tsx
{debouncedCheck !== null && (
  <SlotAvailabilityIndicator checking={checkingSlot} status={slotStatus} />
)}
```

Disable the submit button when the slot is confirmed unavailable:
```tsx
<Button
  type="submit"
  className="w-full bg-violet-600 hover:bg-violet-700 text-white font-medium"
  disabled={isLoading || slotStatus?.available === false}
>
```

### 6-E: Deposit preview panel (inline)

When `activeRules` has at least one rule and an artist is selected, show an estimated
deposit amount so the user knows what to expect before submitting.

Read the existing deposit rule selector JSX. Below the deposit rule `<Select>`, add:

```tsx
{/* Deposit preview — shown when a rule is selected and an artist + duration are known */}
{watchedDepositRuleId && watchedDepositRuleId !== "none" && watchedDuration > 0 && (
  <DepositPreview
    ruleId={watchedDepositRuleId}
    durationMinutes={watchedDuration}
    activeRules={activeRules}
    hourlyRate={selectedArtist?.hourlyRate ?? null}
  />
)}
```

```tsx
function DepositPreview({
  ruleId,
  durationMinutes,
  activeRules,
  hourlyRate,
}: {
  ruleId:          string;
  durationMinutes: number;
  activeRules:     DepositRuleResponse[];
  hourlyRate:      number | null;
}) {
  const rule = activeRules.find((r) => r.id === ruleId);
  if (!rule) return null;

  // Mirror the backend DepositCalculator logic (no new dependency — inline the simple math)
  let estimated: number | null = null;
  if (rule.amountFixed !== null) {
    estimated = rule.amountFixed;
  } else if (rule.amountPercent !== null && hourlyRate !== null) {
    const sessionHours = durationMinutes / 60;
    estimated = (sessionHours * hourlyRate * rule.amountPercent) / 100;
  }

  if (estimated === null) return null;

  return (
    <div className="flex items-center justify-between rounded-md
                    bg-muted/40 border border-border/30 px-3 py-2">
      <span className="text-xs text-muted-foreground">Estimated deposit</span>
      <span className="text-sm font-semibold tabular-nums">
        €{estimated.toFixed(2)}
      </span>
    </div>
  );
}
```

Also add `watchedDepositRuleId` to the `useWatch` calls:
```tsx
const watchedDepositRuleId = useWatch({ control, name: "depositRuleId" });
```

Import `DepositRuleResponse` type from `@/features/deposit-rules/depositRule.types`.

### 6-F: Button styling — brand-colored primary CTA

The current `<Button type="submit" className="w-full">` uses the default shadcn
variant which may render as near-white on dark themes. Override explicitly:

```tsx
<Button
  type="submit"
  className="w-full bg-violet-600 hover:bg-violet-700 text-white font-medium
             disabled:bg-violet-600/50"
  disabled={isLoading || slotStatus?.available === false}
>
  {isLoading ? (
    <><Loader2 className="h-4 w-4 animate-spin mr-2" aria-hidden="true" />Booking…</>
  ) : (
    "Request Appointment"
  )}
</Button>
```

Add helper text below the button explaining the request flow:

```tsx
<p className="text-center text-[11px] text-muted-foreground/60">
  Your artist will confirm availability within 24 hours.
</p>
```

### 6-G: Notes placeholder — improve copy

```tsx
<Textarea
  id="notes"
  rows={3}
  placeholder="Style, size, placement, reference images, skin concerns…"
  {...register("notes")}
  className="resize-none"
/>
```

### 6-H: Complete updated `BookAppointmentForm` import block

After all changes, the import section should include:

```tsx
import { useEffect, useMemo, useState } from "react";
import { useForm, Controller, useWatch } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import {
  AlertCircle, Banknote, CheckCircle2, Loader2
} from "lucide-react";
import { Button }   from "@/shared/components/ui/button";
import { Input }    from "@/shared/components/ui/input";
import { Textarea } from "@/shared/components/ui/textarea";
import { Label }    from "@/shared/components/ui/label";
import {
  Select, SelectContent, SelectItem,
  SelectTrigger, SelectValue,
} from "@/shared/components/ui/select";
import { useAppSelector }  from "@/app/hooks";
import { useCurrentUser }  from "@/shared/hooks/useCurrentUser";
import { cn }              from "@/shared/utils/cn";
import { Role }            from "@/shared/types/roles";
import {
  useCreateAppointmentMutation,
  useCheckSlotAvailabilityQuery,
} from "../appointmentsApi";
import { useGetArtistsQuery }                   from "@/features/artists/artistsApi";
import { useGetClientsQuery, useGetMyClientQuery } from "@/features/clients/clientsApi";
import { useGetDepositRulesQuery }              from "@/features/deposit-rules/depositRulesApi";
import { PaymentMethodSelector }               from "@/features/payments/components/PaymentMethodSelector";
import type { AppointmentResponse, CheckSlotAvailabilityParams, SlotAvailabilityResponse }
  from "../appointment.types";
import type { ArtistResponse }       from "@/features/artists/artistsApi";
import type { DepositRuleResponse }  from "@/features/deposit-rules/depositRule.types";
```

---

## Section 7 — Frontend: `BookPage.tsx` — layout and heading fix

Read the current file. Apply these targeted changes:

### 7-A: Remove the redundant "Book an Appointment" page heading

The `Card` already has a meaningful title. The page-level heading duplicates it.
Remove the `<div className="flex items-center gap-2">` block containing `PenLine`
and "Book an Appointment".

The page-level brand context (knowing you're on the booking page) comes from the
route/layout. The card heading "New appointment" is sufficient.

**Updated `BookPage`:**

```tsx
import { Link } from "react-router-dom";
import { ChevronLeft } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { BookingWidget } from "@/features/booking/components/BookingWidget";
import { BookAppointmentForm } from "./BookAppointmentForm";
import { MyBookingsSection } from "./MyBookingsSection";

export function BookPage() {
  return (
    <BookingWidget>
      <div className="bg-background flex items-start justify-center px-4 py-12">
        <div className="w-full max-w-md space-y-6">

          {/* Back navigation */}
          <Link
            to="/"
            className="inline-flex items-center gap-1 text-xs text-muted-foreground
                       hover:text-foreground transition-colors"
          >
            <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
            Back
          </Link>

          <Card>
            <CardHeader className="pb-2">
              <CardTitle className="text-base">Book an appointment</CardTitle>
              <p className="text-xs text-muted-foreground">
                Select an artist, date, and session length. Your booking is a
                request — the studio will confirm within 24 hours.
              </p>
            </CardHeader>
            <CardContent>
              <BookAppointmentForm />
            </CardContent>
          </Card>

          <MyBookingsSection />
        </div>
      </div>
    </BookingWidget>
  );
}
```

**Note:** The existing `BookPage` tests assert:
- `"Book an Appointment"` heading is present — UPDATE this assertion.
- `"New appointment"` card title is present — UPDATE to `"Book an appointment"`.
- `"My bookings"` section — no change needed.

### 7-B: Add `<h1>` for SEO and screen readers

Wrap the `CardTitle` text in a proper `<h1>`:
```tsx
<CardTitle className="text-base">
  <h1 className="text-base font-semibold">Book an appointment</h1>
</CardTitle>
```

---

## Section 8 — Tests: update and add

### 8-A: Update `BookPage.test.tsx` — fix broken assertions

The following tests will fail after the heading changes. Fix them:

```typescript
// OLD (will fail after heading removal):
it("renders the 'Book an Appointment' heading", () => {
  renderBookPage();
  expect(screen.getByText("Book an Appointment")).toBeInTheDocument();
});

it("renders the 'New appointment' card title", () => {
  renderBookPage();
  expect(screen.getByText("New appointment")).toBeInTheDocument();
});

// NEW — replace with:
it("renders the 'Book an appointment' page heading", () => {
  renderBookPage();
  expect(screen.getByRole("heading", { name: /book an appointment/i })).toBeInTheDocument();
});

it("renders descriptive subtitle about the request flow", () => {
  renderBookPage();
  expect(screen.getByText(/the studio will confirm within 24 hours/i)).toBeInTheDocument();
});

it("renders a back navigation link", () => {
  renderBookPage();
  expect(screen.getByRole("link", { name: /back/i })).toBeInTheDocument();
});
```

### 8-B: Add MSW handler for slot check

In the test file's `setupServer` call, add:

```typescript
http.get("http://localhost/api/v1/appointments/check-slot", () =>
  HttpResponse.json({ available: true, reason: null }),
),
```

### 8-C: Add new tests for form improvements

```typescript
it("artist dropdown items include artist specializations", async () => {
  renderForm();
  const user = userEvent.setup();
  await screen.findByText("Luna Artista");
  await user.click(screen.getByLabelText("Select artist"));
  expect(await screen.findByText("Neo-trad")).toBeInTheDocument();
});

it("artist dropdown includes a search input", async () => {
  renderForm();
  const user = userEvent.setup();
  await screen.findByText("Luna Artista");
  await user.click(screen.getByLabelText("Select artist"));
  expect(screen.getByPlaceholderText(/search artists/i)).toBeInTheDocument();
});

it("artist search filters by name", async () => {
  // Add a second artist to the MSW response for this test
  server.use(
    http.get("http://localhost/api/v1/artists", () =>
      HttpResponse.json([
        ARTIST,
        { ...ARTIST, id: "a-002", firstName: "Marco", lastName: "Rivera",
          specializations: "Blackwork" },
      ]),
    ),
  );
  const user = userEvent.setup();
  renderForm();
  await screen.findByText("Luna Artista");
  await user.click(screen.getByLabelText("Select artist"));
  await user.type(screen.getByPlaceholderText(/search artists/i), "marco");
  expect(screen.getByText("Marco Rivera")).toBeInTheDocument();
  expect(screen.queryByText("Luna Artista")).not.toBeInTheDocument();
});

it("Duration field is a select not a number input", async () => {
  renderForm();
  await screen.findByText("Luna Artista");
  // The old test asserted input.value === "60". New control is a Select.
  expect(screen.getByRole("combobox", { name: /session length/i })).toBeInTheDocument();
});

it("Duration select shows preset durations including '1 hour'", async () => {
  const user = userEvent.setup();
  renderForm();
  await screen.findByText("Luna Artista");
  await user.click(screen.getByRole("combobox", { name: /session length/i }));
  expect(await screen.findByRole("option", { name: /1 hour/i })).toBeInTheDocument();
  expect(screen.getByRole("option", { name: /30 min/i })).toBeInTheDocument();
});

it("shows 'This slot is available' indicator after artist + date + duration are set", async () => {
  const user = userEvent.setup();
  renderForm();

  await screen.findByText("Luna Artista");
  await user.click(screen.getByLabelText("Select artist"));
  await user.click(await screen.findByRole("option", { name: "Luna Artista" }));

  const futureDate = new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 16);
  await user.type(screen.getByLabelText(/date.*time/i), futureDate);

  // Slot check should fire and show the available indicator
  expect(await screen.findByText(/this slot is available/i)).toBeInTheDocument();
});

it("shows unavailable reason when slot check returns false", async () => {
  server.use(
    http.get("http://localhost/api/v1/appointments/check-slot", () =>
      HttpResponse.json({ available: false, reason: "That slot is already booked." }),
    ),
  );
  const user = userEvent.setup();
  renderForm();

  await screen.findByText("Luna Artista");
  await user.click(screen.getByLabelText("Select artist"));
  await user.click(await screen.findByRole("option", { name: "Luna Artista" }));

  const futureDate = new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 16);
  await user.type(screen.getByLabelText(/date.*time/i), futureDate);

  expect(await screen.findByText(/that slot is already booked/i)).toBeInTheDocument();
});

it("submit button is disabled when slot is unavailable", async () => {
  server.use(
    http.get("http://localhost/api/v1/appointments/check-slot", () =>
      HttpResponse.json({ available: false, reason: "Slot already booked." }),
    ),
  );
  const user = userEvent.setup();
  renderForm();

  await screen.findByText("Luna Artista");
  await user.click(screen.getByLabelText("Select artist"));
  await user.click(await screen.findByRole("option", { name: "Luna Artista" }));
  await user.type(
    screen.getByLabelText(/date.*time/i),
    new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 16),
  );

  await screen.findByText(/slot already booked/i);
  expect(screen.getByRole("button", { name: /request appointment/i }))
    .toBeDisabled();
});

it("required fields have required asterisk in their label", async () => {
  renderForm();
  await screen.findByText("Luna Artista");
  // aria-hidden="true" on asterisk — check for label text + asterisk via testId or content
  const artistLabel = screen.getByText("Artist", { selector: "label" });
  expect(artistLabel.textContent).toContain("*");
});

it("helper text below button explains 24-hour confirmation", async () => {
  renderForm();
  await screen.findByText("Luna Artista");
  expect(screen.getByText(/artist will confirm.*24 hours/i)).toBeInTheDocument();
});

it("Notes placeholder provides helpful guidance", async () => {
  renderForm();
  expect(screen.getByPlaceholderText(/style.*size.*placement/i)).toBeInTheDocument();
});

it("existing Duration test — update to use select not input", async () => {
  renderForm();
  await screen.findByText("Luna Artista");
  // Old assertion: expect(input.value).toBe("60");
  // New: the default 60min option should be selected
  const durationSelect = screen.getByRole("combobox", { name: /session length/i });
  expect(durationSelect).toBeInTheDocument();
  // The displayed value should reflect 60 minutes
  expect(durationSelect).toHaveTextContent(/1 hour/i);
});
```

**Delete or update** the old duration test that checks `input.value === "60"` since
that field is now a `<Select>` (combobox).

---

## Section 9 — Architecture docs update

After all changes, update `docs/claude/architecture.md`:

1. **Decisions Log** — add:
   ```
   | BookAppointmentForm — Duration control | Changed from <Input type="number"> to <Select> with preset values | Prevents invalid durations; consistent with Artist selector style |
   | Artist.IsActive filter | Added to GetArtistsQuery; seed records set IsActive=false | Prevents test/seed data appearing in client-facing dropdowns |
   | Slot availability pre-check | CheckSlotAvailabilityQuery via GET /appointments/check-slot | Inline feedback before submit reduces failed bookings and support load |
   | Artist avatar in booking dropdown | AvatarUrl added to Artist entity and ArtistResponse | Tattoo booking is visual and personal; avatar increases conversion |
   ```

2. **Feature Module Map** — update Feature: Appointments to note slot availability check.

3. **AllowAnonymous / IgnoreQueryFilters tables** — no new entries (slot check uses
   `ClientAndAbove` and the standard tenant-scoped DB context).

---

## Section 10 — Build checklist

Run in order. Fix every error before moving on.

```bash
cd "Pena e Arte"

# 1. Backend build (new entity fields, query, endpoint, migration)
dotnet build --verbosity minimal

# 2. Migrations
dotnet ef database update \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API

# 3. Backend unit tests
dotnet test

# 4. Frontend type check
cd frontend && pnpm tsc --noEmit

# 5. Lint
pnpm lint

# 6. All frontend tests must pass
pnpm test --run
```

All six commands must exit 0.

---

## Summary of Changes

### Critical bugs fixed:
1. **Duplicate artist entries** — deduplication in `GetArtistsQuery` + client-side `useMemo` dedup
2. **Test/seed data visible** — `Artist.IsActive` filter on all public-facing queries
3. **SelectTrigger wrong icon** — fixed in `select.tsx` to use `ChevronDown` with open/close rotation

### UX improvements:
4. **Duration → `<Select>` with preset values** — eliminates invalid inputs
5. **Real-time slot availability check** — inline green/red indicator before submit
6. **Artist avatars + search in dropdown** — rich combobox replaces plain text list
7. **Required field markers** — `*` on required fields + "* Required" legend
8. **Primary button color** — explicit `bg-violet-600` instead of theme-dependent default
9. **Deposit preview** — shows estimated deposit before submit
10. **Helper text + back link** — explains request flow; navigation context restored
11. **Notes placeholder** — actionable guidance replaces vague placeholder
12. **Redundant heading** — removed page-level duplicate; card subtitle explains the flow

### Backend additions:
13. **`Artist.IsActive`, `Artist.AvatarUrl`** — entity and migration
14. **`CheckSlotAvailabilityQuery`** — new lightweight endpoint

---

## Hard Rules Reminder

- No new npm or NuGet packages. `AlertCircle` is from `lucide-react` (already installed).
- No `useEffect` for data fetching — the slot-check `useEffect` is a debounce timer, not a fetch.
- No `any`. All new types must be fully typed.
- No default exports on components.
- No TypeScript `enum` — use `as const` objects.
- Every new endpoint has a FluentValidation validator (`CheckSlotAvailabilityValidator`).
- All tests green before the session ends.
