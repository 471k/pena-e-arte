# Overnight Prompt — Slug UI, PDF Branding, and UI Polish Backlog
> Date: 2026-06-19
> Three independent workstreams. Complete them in order (A → B → C).
> Run `dotnet test && pnpm test --run` between workstreams to catch regressions.

---

## Pre-flight

```bash
dotnet build           # must be clean
dotnet test            # record baseline pass count
pnpm tsc --noEmit      # record pre-existing errors
pnpm test --run        # record baseline pass count
```

Read `CLAUDE.md` before writing any code.
Read `docs/claude/backend.md` before touching C#.
Read `docs/claude/frontend.md` before touching TypeScript.

---

# Workstream A — Slug Self-Edit UI (SP-02 step 7)

## Context

`UpdateStudioSlugCommand` is fully implemented with FluentValidation and the endpoint
`PATCH /api/v1/studios/{id}/slug` is already mapped in `StudioEndpoints.cs`.
The backend enforces: lowercase + numbers + hyphens only, max 60 chars, globally unique,
one-time-only via `Studio.SlugLockedAt DateTime?`.

The only things missing are:
1. `SlugLockedAt` is not in `StudioResponse` (DTO + handler mapping)
2. No `updateStudioSlug` mutation in `studiosApi.ts`
3. The slug is rendered read-only in `StudioProfilePage.tsx` with no edit affordance

---

## A1 — Backend: add `SlugLockedAt` to `StudioResponse`

**File:** `Pena_e_Arte.Contracts/Responses/StudioResponse.cs`

Add `DateTime? SlugLockedAt` to the record. Since `StudioResponse` is a positional record,
append it at the end to minimise call-site breakage:

```csharp
public record StudioResponse(
    Guid      Id,
    string    Name,
    string    Slug,
    string    City,
    double    Latitude,
    double    Longitude,
    bool      ShowPlatformBranding,
    bool      AllowBrandingRemoval,
    DateTime  TrialExpiresAt,
    DateTime  CreatedAt,
    bool      IsActive,
    DateTime? SlugLockedAt);      // ← new
```

**File:** `Pena_e_Arte.Application/Studios/Queries/GetMyStudioQuery.cs`

Add `studio.SlugLockedAt` as the last argument in the `StudioResponse(...)` constructor call:

```csharp
return new StudioResponse(
    studio.Id, studio.Name, studio.Slug, studio.City,
    studio.Latitude, studio.Longitude,
    studio.ShowPlatformBranding,
    allowBrandingRemoval,
    studio.TrialExpiresAt, studio.CreatedAt, studio.IsActive,
    studio.SlugLockedAt);         // ← new
```

**Also update every other handler that constructs `StudioResponse`.**
Search for `new StudioResponse(` across the Application project and add `studio.SlugLockedAt`
(or `null`) as the final argument wherever the positional constructor is used.
Common locations: `GetStudioByIdQuery`, `RegisterStudioCommand`, `UpdateMyStudioCommand`,
`UpdateStudioBrandingCommand`. Read each one before editing.

---

## A2 — Frontend: add mutation and type update

**File:** `frontend/src/features/studios/studiosApi.ts`

Add `slugLockedAt: string | null` to the `StudioResponse` interface:

```ts
export interface StudioResponse {
  id:                   string;
  name:                 string;
  slug:                 string;
  city:                 string;
  latitude:             number;
  longitude:            number;
  showPlatformBranding: boolean;
  allowBrandingRemoval: boolean;
  trialExpiresAt:       string;
  createdAt:            string;
  isActive:             boolean;
  slugLockedAt:         string | null;   // ← new
}
```

Add the `updateStudioSlug` mutation inside the `endpoints` builder:

```ts
updateStudioSlug: builder.mutation<void, { id: string; newSlug: string }>({
  query: ({ id, newSlug }) => ({
    url:    `studios/${id}/slug`,
    method: "PATCH",
    body:   { newSlug },
  }),
  invalidatesTags: ["Studio"],
}),
```

Export the hook at the bottom:

```ts
export const {
  // ... existing exports ...
  useUpdateStudioSlugMutation,
} = studiosApi;
```

---

## A3 — Frontend: wire slug edit in `StudioProfilePage.tsx`

**File:** `frontend/src/features/studios/components/StudioProfilePage.tsx`

Read the full file before editing. The slug is currently rendered as plain text inside a `<Card>`.
Replace the slug display with an inline edit card that:

- Shows the current slug as text
- If `studio.slugLockedAt` is `null` (slug was never changed): shows an "Edit" button that expands an input
- If `studio.slugLockedAt` is not null: shows a locked badge "Slug locked — can only be changed once"
- Validates on the client before submitting (same rules as backend: `^[a-z0-9-]+$`, max 60 chars)
- Calls `useUpdateStudioSlugMutation` on submit
- Shows a success toast and collapses the input on success
- Shows the server error (422 body) on failure (e.g. "This slug is already taken")

Add the imports and state at the top of the component:

```tsx
import { useUpdateStudioSlugMutation } from "../studiosApi";
// ... existing imports ...
```

Add state for the inline edit:

```tsx
const [slugEditing, setSlugEditing] = useState(false);
const [slugInput,   setSlugInput]   = useState("");
const [slugError,   setSlugError]   = useState<string | null>(null);

const [updateStudioSlug, { isLoading: slugSaving }] = useUpdateStudioSlugMutation();
```

Slug validation helper (local, no useEffect needed):

```tsx
function validateSlug(value: string): string | null {
  if (!value)                          return "Slug is required.";
  if (value.length > 60)               return "Slug must be 60 characters or fewer.";
  if (!/^[a-z0-9-]+$/.test(value))     return "Slug may only contain lowercase letters, numbers, and hyphens.";
  return null;
}
```

Slug save handler:

```tsx
async function handleSlugSave() {
  const err = validateSlug(slugInput);
  if (err) { setSlugError(err); return; }
  setSlugError(null);
  try {
    await updateStudioSlug({ id: studio!.id, newSlug: slugInput }).unwrap();
    toast.success("Studio URL updated.");
    setSlugEditing(false);
  } catch (e: unknown) {
    const msg =
      e && typeof e === "object" && "data" in e &&
      e.data && typeof e.data === "object" && "message" in e.data
        ? String((e.data as { message: string }).message)
        : "Failed to update slug.";
    setSlugError(msg);
  }
}
```

Replace the slug info card:

```tsx
{studio && (
  <Card>
    <CardContent className="py-3 px-4 space-y-2">
      {/* Studio URL row */}
      <div className="flex items-center gap-2 flex-wrap">
        <span className="text-xs font-semibold text-foreground">Studio URL:</span>
        {!slugEditing ? (
          <>
            <span className="font-mono text-xs text-foreground/80">{studio.slug}</span>
            {studio.slugLockedAt ? (
              <span className="text-xs text-muted-foreground italic ml-1">
                · locked
              </span>
            ) : (
              <Button
                variant="ghost"
                size="sm"
                className="h-6 px-2 text-xs"
                onClick={() => { setSlugInput(studio.slug); setSlugEditing(true); setSlugError(null); }}
              >
                Edit
              </Button>
            )}
          </>
        ) : (
          <div className="flex items-center gap-2 flex-1 min-w-0">
            <Input
              value={slugInput}
              onChange={(e) => { setSlugInput(e.target.value.toLowerCase()); setSlugError(null); }}
              className="h-7 text-xs font-mono w-48"
              placeholder="my-studio-slug"
              maxLength={60}
              aria-label="New studio URL slug"
              aria-invalid={!!slugError}
              aria-describedby={slugError ? "slug-error" : undefined}
            />
            <Button
              size="sm"
              className="h-7 text-xs"
              onClick={handleSlugSave}
              disabled={slugSaving || !slugInput}
            >
              {slugSaving ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : "Save"}
            </Button>
            <Button
              variant="ghost"
              size="sm"
              className="h-7 text-xs"
              onClick={() => setSlugEditing(false)}
              disabled={slugSaving}
            >
              Cancel
            </Button>
          </div>
        )}
        <span className="text-foreground/40">·</span>
        <span className="text-xs text-foreground/70">
          Registered {new Date(studio.createdAt).toLocaleDateString("en-GB")}
        </span>
      </div>

      {slugError && (
        <p id="slug-error" className="text-xs text-destructive">{slugError}</p>
      )}

      {studio.slugLockedAt && (
        <p className="text-xs text-muted-foreground">
          Studio URL was changed on {new Date(studio.slugLockedAt).toLocaleDateString("en-GB")}.
          URLs can only be changed once.
        </p>
      )}
    </CardContent>
  </Card>
)}
```

---

## A4 — Tests: `StudioProfilePage.test.tsx`

Add inside the "after data loads" describe block:

```ts
it("shows an Edit button when slug is not locked", async () => {
  // Use a fixture where slugLockedAt is null
  renderPage();
  await waitForForm();
  expect(screen.getByRole("button", { name: /edit/i })).toBeInTheDocument();
});

it("shows locked indicator when slug is locked", async () => {
  server.use(
    http.get("http://localhost/api/v1/studios/me", () =>
      HttpResponse.json({ ...STUDIO_FIXTURE, slugLockedAt: "2025-06-01T00:00:00Z" }),
    ),
  );
  renderPage();
  await waitForForm();
  expect(screen.getByText(/locked/i)).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /edit/i })).not.toBeInTheDocument();
});

it("shows slug input when Edit is clicked", async () => {
  const user = userEvent.setup();
  renderPage();
  await waitForForm();
  await user.click(screen.getByRole("button", { name: /edit/i }));
  expect(screen.getByLabelText(/new studio url slug/i)).toBeInTheDocument();
});

it("shows validation error for invalid slug characters", async () => {
  const user = userEvent.setup();
  renderPage();
  await waitForForm();
  await user.click(screen.getByRole("button", { name: /edit/i }));
  await user.clear(screen.getByLabelText(/new studio url slug/i));
  await user.type(screen.getByLabelText(/new studio url slug/i), "UPPERCASE INVALID!");
  await user.click(screen.getByRole("button", { name: /save/i }));
  expect(screen.getByText(/lowercase letters, numbers, and hyphens/i)).toBeInTheDocument();
});

it("calls updateStudioSlug and shows success on valid slug", async () => {
  const patchSpy = vi.fn();
  server.use(
    http.patch("http://localhost/api/v1/studios/studio-001/slug", async ({ request }) => {
      patchSpy(await request.json());
      return new HttpResponse(null, { status: 204 });
    }),
  );
  const user = userEvent.setup();
  renderPage();
  await waitForForm();
  await user.click(screen.getByRole("button", { name: /edit/i }));
  await user.clear(screen.getByLabelText(/new studio url slug/i));
  await user.type(screen.getByLabelText(/new studio url slug/i), "new-slug");
  await user.click(screen.getByRole("button", { name: /save/i }));
  await waitFor(() => expect(patchSpy).toHaveBeenCalledWith({ newSlug: "new-slug" }));
});
```

> Add `slugLockedAt: null` to the existing `STUDIO_FIXTURE` (or whatever the test fixture is
> called) so that the slug-unlocked tests don't break when the new field is required.

---

# Workstream B — PDF Branding Footer (SP-03 step 3)

## Context

`ConsentFormPdfService.cs` uses QuestPDF to generate the consent PDF. The service does NOT
currently check `ShowPlatformBranding`. SP-03 requires a conditional "Generated via Pena e Artë"
footer line in the PDF when the studio flag is true.

`ConsentFormPdfData` is a positional record in `IConsentFormPdfService.cs`.
The `SignConsentFormHandler.TryGeneratePdfAsync` constructs it without `ShowPlatformBranding`.

---

## B1 — Add `ShowPlatformBranding` to `ConsentFormPdfData`

**File:** `Pena_e_Arte.Domain/Interfaces/IConsentFormPdfService.cs`

```csharp
public record ConsentFormPdfData(
    string   StudioName,
    string   ClientFullName,
    string   ArtistFullName,
    DateTime AppointmentDate,
    string   SignatureText,
    DateTime SignedAt,
    bool     ShowPlatformBranding = true);   // ← new, defaults true so callers that
                                              //   don't pass it yet stay safe

public interface IConsentFormPdfService
{
    byte[] Generate(ConsentFormPdfData data);
}
```

---

## B2 — Pass `ShowPlatformBranding` in the handler

**File:** `Pena_e_Arte.Application/ConsentForms/Commands/SignConsentFormCommand.cs`

In `TryGeneratePdfAsync`, the `studio` local variable is already fetched. Update the
`ConsentFormPdfData` constructor call:

```csharp
ConsentFormPdfData data = new(
    StudioName:           studio?.Name ?? "Studio",
    ClientFullName:       client is null ? "Client" : $"{client.FirstName} {client.LastName}",
    ArtistFullName:       artist is null ? "Artist" : $"{artist.FirstName} {artist.LastName}",
    AppointmentDate:      appointment.Date,
    SignatureText:        form.SignatureData ?? string.Empty,
    SignedAt:             form.SignedAt ?? DateTime.UtcNow,
    ShowPlatformBranding: studio?.ShowPlatformBranding ?? true);   // ← new
```

---

## B3 — Conditional footer in the PDF service

**File:** `Pena_e_Arte.Infrastructure/Services/ConsentFormPdfService.cs`

After the existing footer note (the "legally binding digital consent record" line, which
should always appear), add a conditional branding line:

```csharp
// ── Footer note ──────────────────────────────────────────
col.Item().PaddingTop(8).Text(
    "This document was generated automatically by Pena e Artë Studio Platform " +
    "and is a legally binding digital consent record.")
    .FontSize(8).FontColor("#aaaaaa").Italic();

// ── Conditional branding footer ──────────────────────────
// SP-03: show "Generated via" line only when studio has branding enabled.
if (d.ShowPlatformBranding)
{
    col.Item().AlignRight().Text("Generated via Pena e Artë · penaearte.com")
        .FontSize(8).FontColor("#bbbbbb").Italic();
}
```

---

## B4 — Tests

**File:** `tests/Pena_e_Arte.UnitTests/ConsentForms/SignConsentFormHandlerTests.cs`

Read the existing test file to understand the mock setup, then add:

```csharp
[Fact]
public async Task TryGeneratePdfAsync_PassesShowPlatformBranding_True_WhenStudioFlagIsTrue()
{
    // Arrange: set up studio with ShowPlatformBranding = true
    // Capture the ConsentFormPdfData passed to pdfService.Generate(...)
    ConsentFormPdfData? captured = null;
    _pdfServiceMock
        .Setup(s => s.Generate(It.IsAny<ConsentFormPdfData>()))
        .Callback<ConsentFormPdfData>(d => captured = d)
        .Returns(Array.Empty<byte>());

    // Act: sign the consent form (use the helper already in the test file)
    await SignFormAsync(showPlatformBranding: true);

    // Assert
    Assert.NotNull(captured);
    Assert.True(captured!.ShowPlatformBranding);
}

[Fact]
public async Task TryGeneratePdfAsync_PassesShowPlatformBranding_False_WhenStudioFlagIsFalse()
{
    ConsentFormPdfData? captured = null;
    _pdfServiceMock
        .Setup(s => s.Generate(It.IsAny<ConsentFormPdfData>()))
        .Callback<ConsentFormPdfData>(d => captured = d)
        .Returns(Array.Empty<byte>());

    await SignFormAsync(showPlatformBranding: false);

    Assert.NotNull(captured);
    Assert.False(captured!.ShowPlatformBranding);
}
```

> Adapt the test helper pattern to what already exists in the file.
> If the existing tests don't have a `showPlatformBranding` parameter, extend the
> studio fixture (or mock setup) to control `ShowPlatformBranding`. Do not break
> existing tests — the default value of `true` in `ConsentFormPdfData` means existing
> constructors that omit the field still compile.

Also add a unit test for the PDF service itself:

```csharp
[Fact]
public void Generate_DoesNotIncludeBrandingText_WhenShowPlatformBrandingIsFalse()
{
    // Arrange
    ConsentFormPdfService svc = new();
    ConsentFormPdfData data = new(
        StudioName:           "Test Studio",
        ClientFullName:       "Ana Costa",
        ArtistFullName:       "João Silva",
        AppointmentDate:      DateTime.UtcNow,
        SignatureText:        "Ana Costa",
        SignedAt:             DateTime.UtcNow,
        ShowPlatformBranding: false);

    // Act — should not throw, just generate PDF without branding footer
    byte[] pdf = svc.Generate(data);

    // Assert — PDF bytes are non-empty; we trust QuestPDF renders correctly.
    // The branding text check is a render concern, not testable via raw bytes without parsing.
    Assert.NotEmpty(pdf);
}
```

---

# Workstream C — UI Polish Backlog

## Standard skeleton / empty-state pattern

Apply this pattern uniformly across all pages listed below.

### Loading state: replace Loader2 spinner with structural skeleton

**If the page currently shows this:**
```tsx
{isLoading && (
  <div className="flex items-center justify-center py-16 gap-2">
    <Loader2 className="h-5 w-5 animate-spin" />
    <span className="text-sm">Loading…</span>
  </div>
)}
```

**Replace with a component named `{PageName}Skeleton`:**
```tsx
function ExamplePageSkeleton() {
  return (
    <div aria-label="Loading {description}">
      {/* Mirror the page's real content shape with Skeleton blocks */}
      {/* Card list skeleton: */}
      <div className="space-y-3">
        {Array.from({ length: 3 }).map((_, i) => (
          <Skeleton key={i} className="h-16 w-full rounded-lg" />
        ))}
      </div>
    </div>
  );
}
```

Then in the page:
```tsx
if (isLoading) return <ExamplePageSkeleton />;
```

Skeleton must:
- Have `aria-label="Loading {what is loading}"` on the wrapper div
- Use `Skeleton` from `@/shared/components/ui/skeleton`
- Mirror the SHAPE of the real content (card → card-height skeleton; form → field-height rows; etc.)

### Empty state (no data at all)

For pages with list views, distinguish:
- `data.length === 0` and no active filters → **rich empty state** (icon + heading + CTA)
- `data.length === 0` and filters active → **simple message** (inline text, no icon/CTA)

Rich empty state pattern:
```tsx
{!isLoading && !isError && data.length === 0 && (
  <div className="flex flex-col items-center gap-4 py-20 text-center">
    <IconComponent className="h-10 w-10 text-muted-foreground/50" />
    <div className="space-y-1">
      <p className="text-sm font-medium text-foreground">No {entity name} yet</p>
      <p className="text-xs text-muted-foreground">
        {Descriptive line about what to do next}
      </p>
    </div>
    <Button size="sm" onClick={...}>Create {entity}</Button>
  </div>
)}
```

---

## Page-by-page instructions

**For each page below:** read the current file first, then apply the standard pattern.
If the page already has a structural skeleton using `Skeleton` components (not `Loader2`),
leave the loading state alone and only fix the empty state if it's missing.

### C1 — `SchedulePage.tsx`

**Loading:** Replace the `Loader2` center spinner with a `SchedulePageSkeleton` that
shows the week header row (7 day columns) and 2-3 appointment card skeletons under
"Mon" and "Tue":

```tsx
function SchedulePageSkeleton() {
  return (
    <main className="max-w-3xl mx-auto px-4 py-6 space-y-8" aria-label="Loading schedule">
      {Array.from({ length: 3 }).map((_, i) => (
        <div key={i} className="space-y-2">
          <Skeleton className="h-4 w-20" />
          <Skeleton className="h-14 w-full rounded-lg" />
          {i === 0 && <Skeleton className="h-14 w-full rounded-lg" />}
        </div>
      ))}
    </main>
  );
}
```

**Empty state (week-level):** When the week has NO appointments at all across all 7 days
(every day shows "No appointments"), render a week-level callout instead of 7 identical
"No appointments" lines:

```tsx
const weekHasAppointments = days.some((day) =>
  (appointments ?? []).some((a) => isSameDay(new Date(a.date), day))
);

// Inside <main>, BEFORE the per-day sections:
{!isLoading && !isError && !weekHasAppointments && appointments !== undefined && (
  <div className="flex flex-col items-center gap-3 py-16 text-center">
    <CalendarDays className="h-9 w-9 text-muted-foreground/40" />
    <p className="text-sm font-medium">No appointments this week</p>
    <p className="text-xs text-muted-foreground">
      Use the arrows to navigate to a different week.
    </p>
  </div>
)}
```

When `weekHasAppointments` is true, render the day sections normally (the per-day
"No appointments" message is appropriate for individual empty days).

### C2 — `AppointmentDetailPage.tsx`

**Loading:** Replace the `Loader2` spinner with:

```tsx
function AppointmentDetailSkeleton() {
  return (
    <main className="max-w-lg mx-auto px-4 py-6 space-y-4" aria-label="Loading appointment">
      <div className="rounded-xl border bg-card p-4 space-y-3">
        {Array.from({ length: 5 }).map((_, i) => (
          <div key={i} className="flex justify-between py-1.5">
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-4 w-32" />
          </div>
        ))}
      </div>
      <Skeleton className="h-9 w-full rounded-md" />
    </main>
  );
}
```

No empty state needed — error state ("Appointment not found") already exists and is correct.

### C3 — `ClientDetailPage.tsx`

Read the full file. If the loading state already uses structural `Skeleton` components, leave it.
If it uses a spinner, replace with:

```tsx
function ClientDetailSkeleton() {
  return (
    <main className="... " aria-label="Loading client">
      {/* Avatar + name row */}
      <div className="flex items-center gap-4 p-6">
        <Skeleton className="h-14 w-14 rounded-full" />
        <div className="space-y-1.5">
          <Skeleton className="h-5 w-36" />
          <Skeleton className="h-4 w-24" />
        </div>
      </div>
      {/* Tab bar */}
      <Skeleton className="h-9 w-full" />
      {/* Content area */}
      <div className="p-6 space-y-3">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-12 w-full rounded-lg" />
        ))}
      </div>
    </main>
  );
}
```

### C4 — `ArtistDetailPage.tsx`

Read the file. Apply `ArtistDetailSkeleton` mirroring the page's content shape
(profile card → appointment list or info rows). Use `aria-label="Loading artist"`.

### C5 — `DepositRuleListPage.tsx` + `DepositRuleDetailPage.tsx` + `CreateDepositRulePage.tsx`

Read each file.

For `DepositRuleListPage`:
- Skeleton: 3 × `h-14 w-full rounded-lg` rows
- Rich empty state: `Receipt` icon (or `Percent`) + "No deposit rules yet" + "Create rule" CTA linking to create page

For `DepositRuleDetailPage` and `CreateDepositRulePage`:
- Skeleton: form-shaped — label+input pairs, one per field

### C6 — `IntakeFormListPage.tsx` + `IntakeFormDetailPage.tsx`

Read each file.

For `IntakeFormListPage`:
- Skeleton: 3 × rows
- Rich empty state: `ClipboardList` icon + "No intake forms yet" + "Create form" CTA

For `IntakeFormDetailPage`:
- Skeleton: title bar + section headings + answer rows

### C7 — `ConsentFormListPage.tsx` + `ConsentFormDetailPage.tsx`

Read each file.

For `ConsentFormListPage`:
- Skeleton: 3 × rows
- Rich empty state: `FileCheck` icon + "No signed consent forms yet" + descriptive line
  ("Consent forms appear here after clients sign them during booking.")

For `ConsentFormDetailPage`:
- Skeleton: document-shaped — wide title skeleton, horizontal rule, body text rows

### C8 — `NotificationLogListPage.tsx`

**Read the file first.** This page already uses `Skeleton` for loading rows and already
has channel/date filters. If this is confirmed, **make no changes** — this page is done.
Verify and skip.

### C9 — `BookPage.tsx` (client-facing booking page)

Read the file. Add skeleton matching the booking form shape (studio name, artist picker,
date picker, time slots). Use `aria-label="Loading booking form"`.

### C10 — `IssuerDashboardPage.tsx` + `MrrChart.tsx`

Read both files. The user noted "MrrChart may be basic".

For `IssuerDashboardPage`:
- Add skeleton for the stats cards while data loads (3 × `h-20 w-full` skeleton cards)

For `MrrChart`:
- If the chart is a simple static bar — replace with a `recharts` `AreaChart` showing monthly
  MRR trend. The data comes from RTK Query. Skeleton while loading: `h-48 w-full rounded-lg`.
  If the chart is already using `recharts`, leave it and only add skeleton for the loading state.

### C11 — `IssuerStudioListPage.tsx` + `IssuerStudioDetailPage.tsx`

Read each file.

For `IssuerStudioListPage`:
- Skeleton: 5 × `h-12 w-full` rows (mimics the table or card list)
- Add client-side search input (filter by studio name, `useMemo`) if not already present.
  Rich empty state: `Building2` icon + "No studios registered yet"

For `IssuerStudioDetailPage`:
- Skeleton mirroring the studio profile info sections

### C12 — `PlanManagementPage.tsx`

Read the file. Add skeleton: 3 × plan card shapes (`h-32 w-full rounded-lg`).
Rich empty state if zero plans: "No plans configured yet" + "Create plan" CTA.

### C13 — `StudioPortfolioPage.tsx` + `ArtistPortfolioPage.tsx`

These are public pages (`/s/:slug` and `/artist/:slug`). Read each file.

**Skeleton:** Show a skeleton while the `useGetPublicStudioQuery` / `useGetPublicArtistQuery`
resolves. Use `aria-label="Loading studio page"` / `aria-label="Loading artist page"`.

**SEO meta:** If these pages do not yet have `<title>` and Open Graph meta tags, add them.
These pages do not use React Router's `useMatches`, so inject via a `<head>` update pattern
(check what method, if any, is already used in the project — look for `document.title =` or
`react-helmet` or Vite SSR meta). If the project has no SSR/helmet, set:
```tsx
useEffect(() => {
  document.title = studio
    ? `${studio.name} — Book a Tattoo on Pena e Artë`
    : "Pena e Artë";
}, [studio]);
```

> Note: `useEffect` for `document.title` is acceptable (it is DOM mutation, not data fetching).

**CTA polish:** Ensure the "Book here" CTA button at the top is styled as `variant="default"`,
full-width on mobile, with the studio's booking route (`/book?studioId={studio.studioId}` or
equivalent — check the router for the correct path).

### C14 — `DesignDetailPage.tsx` + `CreateDesignPage.tsx` + `UploadRevisionPage.tsx`

Read each file. Add structural skeleton for loading state. No new empty states needed — these
are detail/create pages that show an error state when data is not found.

### C15 — `MyProfilePage.tsx`

Read the file. Add `MyProfileSkeleton` mirroring the profile form sections.
Use `aria-label="Loading your profile"`.

---

## General rules for all C pages

1. **Read the file** before making any change. Do not guess structure.
2. Import `Skeleton` from `@/shared/components/ui/skeleton` if not already imported.
3. The skeleton wrapper's `aria-label` must include the word "Loading".
4. Do not add new icon imports beyond what the page already imports — use only icons that
   are already in scope on that page for empty states.
5. If the page already has a proper structural skeleton, confirm and move on. Do not rewrite
   a skeleton that is already correct.
6. Do not change any RTK Query call, API shape, or business logic. Skeletons are purely
   presentation changes.
7. `max-w-*` widths must match the page's existing `<main>` constraint.
8. No new npm packages. `Skeleton` is already in the project.

---

## C Tests

For the two most complex changes (SchedulePage and AppointmentDetailPage), add one test each.
For all other pages, the skeleton change is low-risk and does not require a new test — existing
render tests that check for loaded content will continue to pass.

**SchedulePage:**
```ts
it("shows a skeleton while the schedule is loading", () => {
  server.use(
    http.get("http://localhost/api/v1/appointments", () => new HttpResponse(null, { status: 202 }))
  );
  render(<SchedulePageWithProviders />);
  expect(screen.getByLabelText("Loading schedule")).toBeInTheDocument();
});

it("shows a week-level empty state when the week has no appointments", async () => {
  server.use(
    http.get("http://localhost/api/v1/appointments", () => HttpResponse.json([]))
  );
  render(<SchedulePageWithProviders />);
  expect(await screen.findByText(/no appointments this week/i)).toBeInTheDocument();
});
```

**AppointmentDetailPage:**
```ts
it("shows a skeleton while appointment data loads", () => {
  server.use(
    http.get("http://localhost/api/v1/appointments/:id", () => new HttpResponse(null, { status: 202 }))
  );
  render(<AppointmentDetailPageWithProviders />);
  expect(screen.getByLabelText("Loading appointment")).toBeInTheDocument();
});
```

---

# Final Verification

```bash
# Backend — all tests must pass
dotnet test

# TypeScript — no new errors
pnpm tsc --noEmit

# Frontend — all tests must pass
pnpm test --run

# Linting
pnpm lint
```

If any test fails:
1. Check the test output carefully — do not suppress or skip tests.
2. Fix the root cause, not the test assertion, unless the test was already wrong.
3. Do not mark a workstream as complete if any test in its scope is failing.
