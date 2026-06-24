# Overnight Prompt — Studio Settings Full Overhaul (v2)
> Date: 2026-06-19
> **⚠ This prompt supersedes `overnight-prompt-studio-settings-2026-06-19.md`. Do not run both.**
>
> Primary targets:
>   `NotificationBell.tsx` · `NotificationDetailModal.tsx`
>   `StudioProfilePage.tsx` · `NotificationPreferencesCard.tsx`
>   `BrandingSettingsCard.tsx` · `ReferralCodeCard.tsx`
>   `EmbedCodeCard.tsx` · `QrCodeSection.tsx`
>   `index.css` (Leaflet dark mode)
>
> New test files: `BrandingSettingsCard.test.tsx`
> Updated test files: `StudioProfilePage.test.tsx`
> No new npm or NuGet packages. No backend changes.

---

## Pre-flight

```bash
pnpm tsc --noEmit        # record pre-existing errors; don't count them as regressions
pnpm test src/features/studios src/features/notifications --run   # confirm passing baseline
```

---

## Scope table

| # | File | Issue | Fix |
|---|---|---|---|
| 1 | `NotificationBell.tsx` | Dropdown at `z-30` renders BELOW Leaflet map controls at `z-[1000]` | Raise to `z-[1100]` |
| 2 | `NotificationBell.tsx` | Clicking a log item opens modal but dropdown stays open — two surfaces simultaneously | Close dropdown before opening modal |
| 3 | `NotificationDetailModal.tsx` | `log.subject ?? "(no subject)"` leaks null DB value into UI | Better fallback |
| 4 | `StudioProfilePage.tsx` | Header says "Studio Profile"; nav says "Studio Settings" | Match nav label |
| 5 | `StudioProfilePage.tsx` | `max-w-lg` is narrow; slug card text is mid-grey on dark (≈2.5:1) | `max-w-2xl`; stronger text color |
| 6 | `StudioProfilePage.tsx` | Full-screen spinner on load; no structural skeleton | `StudioProfileSkeleton` |
| 7 | `StudioProfilePage.tsx` | Slug card has no label; no map helper text | "Studio URL:" label + hint |
| 8 | `NotificationPreferencesCard.tsx` | "Sms" in column header; no aria-labels; narrow save button; toggle is color-only | Fix all four |
| 9 | `NotificationPreferencesCard.tsx` | "Save preferences" at page bottom with no sticky positioning | Sticky footer + rename |
| 10 | `BrandingSettingsCard.tsx` | Badge "On/Off" + Button = two visually separate controls for one boolean | Replace with single `<Switch>` auto-save |
| 11 | `ReferralCodeCard.tsx` | "Studios referred" — always plural | Singular/plural |
| 12 | `EmbedCodeCard.tsx` | `window.location.origin` can be admin URL ≠ public URL; preview link shows raw localhost URL | `VITE_PUBLIC_URL` fallback; preview link → "Open preview →" button |
| 13 | `EmbedCodeCard.tsx` | No width/height comment in snippet | HTML comment in snippet |
| 14 | `QrCodeSection.tsx` | `items-start` left-aligns QR image | `items-center` |
| 15 | `index.css` | Leaflet map tiles are full light-mode in dark theme | CSS filter dark-mode inversion |

**Out of scope (too large for overnight):**
- Address autocomplete / forward geocoding (requires significant UI + new API surface)
- Tabbed sidebar restructure (major layout refactor)
- Unifying all Leaflet icon weights with other icons
- Logo vs nav icon register mismatch (`PenLine` is the app logo, not a nav icon — by design)

---

## Part 1 — `NotificationBell.tsx`

**File:** `frontend/src/features/notifications/components/NotificationBell.tsx`

### 1a — Raise dropdown z-index above Leaflet

The LocationPicker on Studio Settings places its "My location" button at `z-[1000]`.
Leaflet's internal control pane sits at Leaflet's default z-800 range.
The notification dropdown at `z-30` renders BELOW all of these.

Change line 60:
```tsx
// BEFORE
<div className="absolute right-0 top-full mt-2 w-80 rounded-md border bg-background shadow-lg z-30">

// AFTER
<div className="absolute right-0 top-full mt-2 w-80 rounded-md border bg-background shadow-lg z-[1100]">
```

### 1b — Close dropdown when a notification item is clicked

Currently clicking a notification item sets `selectedLog` to open `NotificationDetailModal`,
but the `isInboxOpen` state stays `true` — both the dropdown and the modal are visible at once.

In the `<button>` click handler for each log item, also close the inbox:

```tsx
// BEFORE (line 83–88)
<button
  key={log.id}
  type="button"
  className="w-full text-left ..."
  onClick={() => setSelectedLog(log)}
  aria-label={`View notification: ${log.subject ?? log.channel}`}
>

// AFTER
<button
  key={log.id}
  type="button"
  className="w-full text-left ..."
  onClick={() => {
    dispatch(toggleInbox());   // ← close dropdown first
    setSelectedLog(log);
  }}
  aria-label={`View notification: ${log.subject ?? log.channel}`}
>
```

### 1c — Add accessible label to the bell button (it already has one — verify)

Line 47 already has `aria-label`. No change needed. Just confirm it's present after your edits.

---

## Part 2 — `NotificationDetailModal.tsx`

**File:** `frontend/src/features/notifications/components/NotificationDetailModal.tsx`

### 2a — Replace `(no subject)` with a meaningful fallback

The `DialogTitle` on line 42 renders raw `"(no subject)"` when an SMS log has no subject field.
SMS messages don't have subjects — show the channel type + event type instead.

Check what fields `NotificationLogResponse` exposes (read `notification.types.ts`). If `eventType`
or similar exists, use it. Otherwise use:

```tsx
// BEFORE
<DialogTitle className="text-left mt-1 leading-snug">
  {log.subject ?? "(no subject)"}
</DialogTitle>

// AFTER
<DialogTitle className="text-left mt-1 leading-snug">
  {log.subject
    ?? (log.channel === "Sms"
        ? "SMS notification"
        : log.channel === "Email"
        ? "Email notification"
        : "Notification")}
</DialogTitle>
```

If `NotificationLogResponse` has an `eventType` field, prefer:
```tsx
{log.subject ?? log.eventType ?? `${log.channel} notification`}
```

---

## Part 3 — `StudioProfilePage.tsx`

**File:** `frontend/src/features/studios/components/StudioProfilePage.tsx`

### 3a — Fix page header title

```tsx
// BEFORE
<span className="font-semibold tracking-tight">Studio Profile</span>

// AFTER
<span className="font-semibold tracking-tight">Studio Settings</span>
```

### 3b — Widen container

```tsx
// BEFORE
<main className="max-w-lg mx-auto px-4 py-6 space-y-4">

// AFTER
<main className="max-w-2xl mx-auto px-4 py-6 space-y-4">
```

### 3c — Replace loading spinner with `StudioProfileSkeleton`

Add `Skeleton` to the shadcn imports at top of file:
```tsx
import { Skeleton } from "@/shared/components/ui/skeleton";
```

Add this component above `StudioProfilePage`:

```tsx
function StudioProfileSkeleton() {
  return (
    <div className="min-h-screen bg-background" aria-label="Loading studio settings">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Building2 className="h-5 w-5 text-muted-foreground" />
        <Skeleton className="h-5 w-32" />
      </header>
      <main className="max-w-2xl mx-auto px-4 py-6 space-y-4">
        <div className="rounded-xl border bg-card p-3">
          <Skeleton className="h-4 w-72" />
        </div>
        <div className="rounded-xl border bg-card p-5 space-y-4">
          <Skeleton className="h-5 w-28" />
          <div className="space-y-1.5">
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-10 w-full rounded-md" />
          </div>
          <div className="space-y-1.5">
            <Skeleton className="h-4 w-16" />
            <Skeleton className="h-48 w-full rounded-md" />
          </div>
          <Skeleton className="h-9 w-full rounded-md" />
        </div>
      </main>
    </div>
  );
}
```

Replace the loading branch:
```tsx
if (isLoading) return <StudioProfileSkeleton />;
```

### 3d — Slug card — add label and improve contrast

The slug info card needs a "Studio URL:" label so the row reads like data, not noise.
Also change the `text-muted-foreground` on secondary text to `text-foreground/70` so the
contrast ratio meets WCAG AA on dark backgrounds (≥ 4.5:1):

```tsx
{studio && (
  <Card>
    <CardContent className="py-3 px-4 flex items-center gap-2 text-sm flex-wrap">
      <span className="text-xs font-semibold text-foreground">Studio URL:</span>
      <span className="font-mono text-xs text-foreground/80">{studio.slug}</span>
      <span className="text-foreground/40">·</span>
      <span className="text-xs text-foreground/70">
        Registered {new Date(studio.createdAt).toLocaleDateString("en-GB")}
      </span>
    </CardContent>
  </Card>
)}
```

> Note: `text-foreground/70` in Tailwind resolves to the foreground color at 70% opacity.
> On a dark background with foreground ≈ #f9fafb, this gives ≈ 70% × white = enough contrast.
> If Tailwind's foreground color doesn't resolve correctly here, use `text-zinc-300` or the
> closest opaque equivalent from the project's color palette.

### 3e — Add map helper text

Inside the studio details Card, below the `<Label>Location</Label>` line and above `<LocationPicker>`:

```tsx
<p className="text-xs text-muted-foreground -mt-1">
  Click the map or drag the pin to update your studio location.
</p>
```

---

## Part 4 — `NotificationPreferencesCard.tsx`

**File:** `frontend/src/features/notifications/components/NotificationPreferencesCard.tsx`

### 4a — Add `CHANNEL_LABELS` and fix "Sms" → "SMS"

After `const CHANNELS: NotificationChannel[] = ["Email", "Sms"];`, add:

```ts
const CHANNEL_LABELS: Record<NotificationChannel, string> = {
  Email: "Email",
  Sms:   "SMS",
};
```

In `<thead>`:
```tsx
// BEFORE
{CHANNELS.map((ch) => (
  <th key={ch} className="...">{ch}</th>
))}

// AFTER
{CHANNELS.map((ch) => (
  <th key={ch} className="...">{CHANNEL_LABELS[ch]}</th>
))}
```

### 4b — Update `ToggleSwitch` — accept `aria-label` + add non-color indicator

The current toggle uses only color to distinguish on/off state (teal vs grey). Add:
1. `aria-label` prop for accessibility
2. A small "✓" checkmark visible inside the thumb when checked, as a non-color indicator

```tsx
function ToggleSwitch({
  checked,
  onChange,
  "aria-label": ariaLabel,
}: {
  checked:      boolean;
  onChange:     () => void;
  "aria-label": string;
}) {
  return (
    <button
      role="switch"
      aria-checked={checked}
      aria-label={ariaLabel}
      onClick={onChange}
      className={[
        "relative inline-flex h-5 w-9 shrink-0 cursor-pointer items-center rounded-full",
        "border-2 border-transparent transition-colors focus-visible:outline-none",
        "focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
        checked ? "bg-primary" : "bg-input",
      ].join(" ")}
    >
      <span
        className={[
          "pointer-events-none relative flex h-4 w-4 items-center justify-center",
          "rounded-full bg-background shadow-lg ring-0 transition-transform",
          checked ? "translate-x-4" : "translate-x-0",
        ].join(" ")}
      >
        {checked && (
          <svg
            viewBox="0 0 8 8"
            className="h-2 w-2 text-primary"
            aria-hidden="true"
          >
            <path
              d="M1 4l2 2 4-4"
              stroke="currentColor"
              strokeWidth="1.5"
              strokeLinecap="round"
              strokeLinejoin="round"
              fill="none"
            />
          </svg>
        )}
      </span>
    </button>
  );
}
```

Update each call site in the table body to pass `aria-label`:

```tsx
<ToggleSwitch
  checked={local[prefKey(type, channel)] ?? true}
  onChange={() => toggle(type, channel)}
  aria-label={`${label} via ${CHANNEL_LABELS[channel]}`}
/>
```

### 4c — Sticky save button + rename

Wrap the `<Button>` in a sticky footer div and rename the label:

```tsx
{/* Replace the current <Button ...> block with: */}
<div className="sticky bottom-0 pt-2 pb-1 bg-card border-t -mx-6 px-6 mt-2">
  <Button
    size="sm"
    className="w-full gap-2"
    onClick={handleSave}
    disabled={saving || !dirty || isLoading}
  >
    {saving
      ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
      : <Save className="h-3.5 w-3.5" />
    }
    Save notification settings
  </Button>
</div>
```

> The `-mx-6 px-6` trick pulls the div to the edges of CardContent's padding so the
> background fills edge-to-edge. Adjust the margin/padding values to match whatever
> CardContent's padding is in the project (typically `p-6`).

---

## Part 5 — `BrandingSettingsCard.tsx`

**File:** `frontend/src/features/studios/components/BrandingSettingsCard.tsx`

### 5a — Check for shadcn/ui Switch

Before making changes, check if `src/shared/components/ui/switch.tsx` exists.

- **If it exists:** import `{ Switch }` from there.
- **If it doesn't exist:** extract the hand-rolled `ToggleSwitch` from `NotificationPreferencesCard.tsx`
  to `src/shared/components/ui/toggle-switch.tsx` and import from there in both files.
  The extracted version should already accept `aria-label` (since Part 4 added it).

### 5b — Replace Badge + Button with a single Switch

```tsx
import { Loader2 }  from "lucide-react";
import { toast }    from "sonner";
import { Switch }   from "@/shared/components/ui/switch";  // or toggle-switch path
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { useGetMyStudioQuery, useUpdateStudioBrandingMutation } from "../studiosApi";

export function BrandingSettingsCard() {
  const { data: studio }               = useGetMyStudioQuery();
  const [updateBranding, { isLoading }] = useUpdateStudioBrandingMutation();

  if (!studio) return null;

  const canToggleOff = studio.allowBrandingRemoval;
  const isDisabled   = isLoading || (!canToggleOff && studio.showPlatformBranding);
  const upgradeHint  = !canToggleOff && studio.showPlatformBranding
    ? "Upgrade your plan to remove platform branding."
    : undefined;

  async function handleToggle() {
    try {
      await updateBranding({
        id:                   studio!.id,
        showPlatformBranding: !studio!.showPlatformBranding,
      }).unwrap();
      toast.success("Branding preference saved.");
    } catch (err: unknown) {
      const message =
        err && typeof err === "object" && "data" in err &&
        err.data && typeof err.data === "object" && "message" in err.data
          ? String((err.data as { message: string }).message)
          : "Could not update branding — upgrade to remove.";
      toast.error(message);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Platform branding</CardTitle>
      </CardHeader>
      <CardContent>
        <div className="flex items-center justify-between gap-4" title={upgradeHint}>
          <div className="space-y-0.5">
            <p className="text-sm font-medium">
              Show "Powered by Pena e Artë" on booking widget
            </p>
            <p className="text-xs text-muted-foreground">
              Displayed in the booking widget footer visible to your clients.
            </p>
            {upgradeHint && (
              <p className="text-xs text-amber-600 dark:text-amber-400 mt-1">
                {upgradeHint}
              </p>
            )}
          </div>

          {isLoading
            ? <Loader2 className="h-4 w-4 animate-spin text-muted-foreground shrink-0" />
            : (
              <Switch
                checked={studio.showPlatformBranding}
                onCheckedChange={handleToggle}
                disabled={isDisabled}
                aria-label="Show platform branding on booking widget"
              />
            )
          }
        </div>
      </CardContent>
    </Card>
  );
}
```

> **Note on `updateBranding` mutation:** Check `studiosApi.ts` to confirm the mutation name
> and request shape for updating branding. Adjust `useUpdateStudioBrandingMutation` and
> the body `{ id, showPlatformBranding }` to match what the API actually expects.

---

## Part 6 — `ReferralCodeCard.tsx`

**File:** `frontend/src/features/studios/components/ReferralCodeCard.tsx`

Singular/plural fix — find the "Studios referred" line:

```tsx
// BEFORE
<p className="text-xs text-muted-foreground">Studios referred</p>

// AFTER
<p className="text-xs text-muted-foreground">
  Studio{stats.redemptionCount !== 1 ? "s" : ""} referred
</p>
```

---

## Part 7 — `EmbedCodeCard.tsx`

**File:** `frontend/src/features/studios/components/EmbedCodeCard.tsx`

### 7a — Use `VITE_PUBLIC_URL` for the embed base URL

```tsx
// BEFORE
const embedUrl = `${window.location.origin}/embed/${studio.slug}`;

// AFTER
const EMBED_BASE = import.meta.env.VITE_PUBLIC_URL ?? window.location.origin;
const embedUrl   = `${EMBED_BASE}/embed/${studio.slug}`;
```

### 7b — Add HTML comment to the snippet

```tsx
// BEFORE
const iframeCode = `<iframe\n  src="${embedUrl}"\n  width="380"\n...`;

// AFTER
const iframeCode = [
  `<!-- Adjust width/height to fit your layout -->`,
  `<iframe`,
  `  src="${embedUrl}"`,
  `  width="380"`,
  `  height="600"`,
  `  frameborder="0"`,
  `  title="Book at ${studio.name}"`,
  `  allow="payment"`,
  `></iframe>`,
].join("\n");
```

### 7c — Replace raw preview link with "Open preview →" button

Currently:
```tsx
<p className="text-xs text-muted-foreground">
  Preview:{" "}
  <a href={embedUrl} target="_blank" rel="noopener noreferrer" className="underline hover:text-foreground">
    {embedUrl}
  </a>
</p>
```

The raw URL leaks `http://localhost:5174/...` in local development. Replace with a button that
doesn't expose the URL string in the UI:

```tsx
<div className="flex items-center gap-2">
  <p className="text-xs text-muted-foreground">
    Preview your booking widget in a new tab.
  </p>
  <Button
    variant="link"
    size="sm"
    className="h-auto p-0 text-xs"
    asChild
  >
    <a href={embedUrl} target="_blank" rel="noopener noreferrer">
      Open preview →
    </a>
  </Button>
</div>
```

> `Button` with `asChild` + `<a>` renders a proper anchor with button styling.
> The URL is still functional (click goes to the right place) but not displayed raw.

---

## Part 8 — `QrCodeSection.tsx`

**File:** `frontend/src/features/studios/components/QrCodeSection.tsx`

Center the QR image:

```tsx
// BEFORE
<div className="flex flex-col items-start gap-4">

// AFTER
<div className="flex flex-col items-center gap-4">
```

---

## Part 9 — Leaflet dark mode CSS

**File:** `frontend/src/index.css` (or wherever global styles live — check `main.tsx` for the
import if unsure)

Add these rules at the end of the file:

```css
/* ── Leaflet dark mode ───────────────────────────────────────────────────── */
/* Invert map tiles in dark theme to avoid the jarring light-mode island.    */
/* The pin icon is re-inverted so it stays visible against dark tiles.       */
.dark .leaflet-tile-pane {
  filter: invert(92%) hue-rotate(180deg) brightness(0.95) saturate(0.85);
}

/* Keep UI controls (zoom, attribution) readable in dark mode */
.dark .leaflet-control-zoom a {
  color: var(--foreground);
  background-color: var(--card);
  border-color: var(--border);
}

/* Prevent the pin SVG (already dark-red) from being inverted */
.dark .leaflet-marker-icon {
  filter: none;
}
```

> These rules use the `.dark` class that Tailwind injects when dark mode is active.
> They do NOT require any new package — only CSS.

---

## Part 10 — Tests

### 10a — Update `StudioProfilePage.test.tsx`

**File:** `frontend/src/features/studios/__tests__/StudioProfilePage.test.tsx`

Find and update the loading-state test:

```ts
// BEFORE
it("shows a loading spinner while studio data is being fetched", () => {
  renderPage();
  expect(screen.getByText(/loading/i)).toBeInTheDocument();
});

// AFTER
it("shows a loading skeleton while studio data is being fetched", () => {
  renderPage();
  expect(screen.getByLabelText("Loading studio settings")).toBeInTheDocument();
});
```

Append new tests inside the "after data loads" describe block:

```ts
it("shows 'Studio Settings' as the page header title", async () => {
  renderPage();
  await waitForForm();   // whatever helper the file already uses to await load
  // Must match the updated header text exactly
  expect(screen.getAllByText("Studio Settings").length).toBeGreaterThan(0);
});

it("shows 'Studio URL:' label in the slug info card", async () => {
  renderPage();
  await waitForForm();
  expect(screen.getByText("Studio URL:")).toBeInTheDocument();
});

it("shows the map helper text below the Location label", async () => {
  renderPage();
  await waitForForm();
  expect(
    screen.getByText(/click the map or drag the pin/i)
  ).toBeInTheDocument();
});
```

> Note: The nav also says "Studio Settings" (from OwnerLayout), so `getAllByText` avoids
> false-negatives from multiple matches. Use `getAllByText` and check `length >= 1`.

### 10b — Create `BrandingSettingsCard.test.tsx`

**File:** `frontend/src/features/studios/__tests__/BrandingSettingsCard.test.tsx`

```ts
import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { studiosApi } from "@/features/studios/studiosApi";
import { BrandingSettingsCard } from "@/features/studios/components/BrandingSettingsCard";

// ── Fixtures ─────────────────────────────────────────────────────────────────

// Import StudioResponse from studiosApi or inline it here.
// Adjust fields to match the actual type if it differs.
type StudioResponse = {
  id: string; name: string; slug: string;
  city: string; latitude: number; longitude: number;
  showPlatformBranding: boolean; allowBrandingRemoval: boolean;
  trialExpiresAt: string; createdAt: string; isActive: boolean;
};

const BASE: StudioResponse = {
  id: "s-001", name: "Ink & Soul", slug: "ink-soul",
  city: "Lisbon", latitude: 38.7, longitude: -9.1,
  showPlatformBranding: true, allowBrandingRemoval: false,
  trialExpiresAt: "2099-01-01T00:00:00Z",
  createdAt: "2025-01-01T00:00:00Z",
  isActive: true,
};

const REMOVABLE:    StudioResponse = { ...BASE, allowBrandingRemoval: true };
const BRANDING_OFF: StudioResponse = { ...REMOVABLE, showPlatformBranding: false };

// ── MSW ──────────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json(BASE)),
);
beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ───────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: { auth: authReducer, [studiosApi.reducerPath]: studiosApi.reducer },
    middleware: (g) => g().concat(studiosApi.middleware),
  });
}

function renderCard() {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <BrandingSettingsCard />
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("BrandingSettingsCard", () => {

  it("renders the card heading", async () => {
    renderCard();
    expect(await screen.findByText(/platform branding/i)).toBeInTheDocument();
  });

  it("shows descriptive text about the branding toggle", async () => {
    renderCard();
    expect(await screen.findByText(/powered by pena/i)).toBeInTheDocument();
  });

  it("switch is checked when showPlatformBranding is true", async () => {
    renderCard();
    const sw = await screen.findByRole("switch");
    expect(sw).toHaveAttribute("aria-checked", "true");
  });

  it("switch is unchecked when showPlatformBranding is false", async () => {
    server.use(http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json(BRANDING_OFF)));
    renderCard();
    const sw = await screen.findByRole("switch");
    expect(sw).toHaveAttribute("aria-checked", "false");
  });

  it("switch is disabled when plan forbids removal and branding is on", async () => {
    // Default fixture: allowBrandingRemoval=false, showPlatformBranding=true
    renderCard();
    const sw = await screen.findByRole("switch");
    expect(sw).toBeDisabled();
  });

  it("switch is enabled when plan allows removal", async () => {
    server.use(http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json(REMOVABLE)));
    renderCard();
    const sw = await screen.findByRole("switch");
    expect(sw).not.toBeDisabled();
  });

  it("shows upgrade hint when plan forbids removal", async () => {
    renderCard();
    expect(await screen.findByText(/upgrade your plan/i)).toBeInTheDocument();
  });

  it("does not show upgrade hint when plan allows removal", async () => {
    server.use(http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json(REMOVABLE)));
    renderCard();
    await screen.findByRole("switch");
    expect(screen.queryByText(/upgrade your plan/i)).not.toBeInTheDocument();
  });

  it("calls the update mutation when switch is clicked", async () => {
    // ⚠ Adjust the PATCH URL to match what useUpdateStudioBrandingMutation actually uses.
    // Check studiosApi.ts for the correct endpoint path and method.
    const updateSpy = vi.fn();
    server.use(
      http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json(REMOVABLE)),
      http.patch("http://localhost/api/v1/studios/s-001/branding", async ({ request }) => {
        const body = await request.json() as Record<string, unknown>;
        updateSpy(body);
        return HttpResponse.json({ ...REMOVABLE, showPlatformBranding: false });
      }),
    );
    const user = userEvent.setup();
    renderCard();
    await user.click(await screen.findByRole("switch"));
    await waitFor(() => expect(updateSpy).toHaveBeenCalledOnce());
    expect(updateSpy).toHaveBeenCalledWith(
      expect.objectContaining({ showPlatformBranding: false }),
    );
  });

});
```

> **Important:** Before running this test, read `studiosApi.ts` to find the actual mutation
> that updates branding (it may be `useUpdateStudioMutation` with partial body, or a dedicated
> endpoint). Adjust the MSW handler URL/method and the mutation import accordingly.

### 10c — Check for existing NotificationBell tests

Check if `frontend/src/features/notifications/__tests__/NotificationBell.test.tsx` exists.

If it exists, add two tests:
```ts
it("notification dropdown renders above Leaflet map controls", async () => {
  // Render NotificationBell; open the dropdown; check the dropdown element
  // has z-index 1100 (via className or computed style)
  const user = userEvent.setup();
  render(<NotificationBellWithProviders />);
  await user.click(screen.getByRole("button", { name: /notifications/i }));
  const dropdown = screen.getByRole("region", { name: /notifications/i })
    ?? document.querySelector("[class*='z-\\[1100\\]']");
  // If the implementation uses Tailwind z-[1100], just assert the panel is visible
  expect(screen.getByText(/view all/i)).toBeVisible();
});

it("dropdown closes when a notification item is clicked", async () => {
  // ...mock a log item, click it, assert dropdown is gone
});
```

If the test file does not exist, skip writing NotificationBell tests — the z-index change is
verifiable visually and the click-to-close behavior is covered by the component logic.

---

## Part 11 — Verify

```bash
# TypeScript — no new errors beyond pre-existing
pnpm tsc --noEmit

# Studio feature tests — all must pass
pnpm test src/features/studios --run

# Notification feature tests (z-index, aria-label changes)
pnpm test src/features/notifications --run

# Full suite smoke-check
pnpm test --run
```

---

## Audit vs Code: what was wrong / already correct

| Audit claim | Reality |
|---|---|
| "Right-side notifications panel, full-width" | No such component — OwnerLayout has NO right sidebar. The audit screenshot showed the `w-80` NotificationBell dropdown |
| "Map modal — wrong visual register" | The LocationPicker is INLINE in the form (h-260px) — not a modal. CSS filter fix in Part 9 addresses the tile contrast |
| "No Save button for Studio Details" | `SubscriptionGatedButton` already exists in StudioProfilePage — the audit missed it while distracted by the z-index chaos |
| "Referral link not shown" | `ReferralCodeCard` already shows the code in a copyable field when one exists; Generate button is conditional |
| "Copy icon button — no aria-label" | `EmbedCodeCard` already has `aria-label="Copy embed code"` on the copy button (line 43) |
| "No loading/skeleton states" | `NotificationBell` shows "Loading…" when open; LocationPicker shows `Loader2` while resolving — acceptable |

---

## Constraints (from CLAUDE.md)

- **No new npm packages.** `Switch` must come from shadcn/ui already installed, or the extracted hand-rolled `ToggleSwitch`. Leaflet dark mode is pure CSS — no new library.
- **No useEffect for data fetching.** The `useEffect` in `NotificationPreferencesCard` that syncs `local` state from fetched `data` is acceptable — it is a derived-state sync, not an API call.
- **TypeScript strict mode** — no `any`, explicit types on every function and variable.
- **No default exports on components** — all component exports use named exports.
- **No business logic in endpoints** — all backend calls go through MediatR. No backend changes in this prompt.
