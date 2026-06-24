# Overnight Prompt — Studio Settings Page Overhaul
> Date: 2026-06-19
> Primary targets: `StudioProfilePage.tsx`, `NotificationPreferencesCard.tsx`,
>                  `BrandingSettingsCard.tsx`, `ReferralCodeCard.tsx`,
>                  `EmbedCodeCard.tsx`, `QrCodeSection.tsx`
> Tests: update `StudioProfilePage.test.tsx`; create `BrandingSettingsCard.test.tsx`
> No new npm or NuGet packages. No backend changes.

---

## Pre-flight

1. Read `CLAUDE.md` and `docs/claude/frontend.md` before making any changes.
2. Run `pnpm tsc --noEmit` — note pre-existing errors; do not count them as regressions.
3. Run `pnpm test src/features/studios` — confirm all existing studio tests pass first.
4. Run `pnpm test src/features/notifications` — confirm passing baseline there too.

---

## Context

The audit screenshot was partially stale. Several items the audit called "missing" or "critical"
are already implemented:

| Audit complaint | Actual state |
|---|---|
| `EmbedCodeCard` "hardcoded localhost" | Already uses `window.location.origin` — not hardcoded |
| "Referral code not shown — hidden behind Generate button" | `ReferralCodeCard` already shows the code in a copyable field when one exists |
| "Disable branding" button does not describe state transition | Button already shows "Disable branding" OR "Enable branding" based on state |

**What IS broken and in scope:**

| File | Issue | Fix |
|---|---|---|
| `StudioProfilePage.tsx` | Header says "Studio Profile"; nav says "Studio Settings" | Change header to "Studio Settings" |
| `StudioProfilePage.tsx` | Container is `max-w-lg` — narrow vs other pages | `max-w-2xl` |
| `StudioProfilePage.tsx` | Full-screen spinner on load; no structural skeleton | `StudioProfileSkeleton` component |
| `StudioProfilePage.tsx` | Slug card has no label — reads as noise | Add "Studio URL:" label |
| `StudioProfilePage.tsx` | No helper text on the Location field | Add hint below Location label |
| `NotificationPreferencesCard.tsx` | Column header renders raw enum value "Sms" | Add `CHANNEL_LABELS` display map → "SMS" |
| `NotificationPreferencesCard.tsx` | `ToggleSwitch` has no accessible name per row | Add `aria-label` to each switch instance |
| `NotificationPreferencesCard.tsx` | "Save preferences" button is narrow; "Save changes" is full-width | Add `w-full` to "Save preferences" |
| `BrandingSettingsCard.tsx` | `<Badge>` showing "On/Off" next to button looks like two controls | Replace Badge + Button with a single `<Switch>` that auto-saves |
| `ReferralCodeCard.tsx` | "Studios referred" — always plural, even for count = 1 | Singular/plural: `Studio{n !== 1 ? 's' : ''} referred` |
| `EmbedCodeCard.tsx` | `window.location.origin` can be admin URL ≠ public embed URL | Use `VITE_PUBLIC_URL ?? window.location.origin` |
| `EmbedCodeCard.tsx` | No width/height comment in snippet | Add HTML comment |
| `QrCodeSection.tsx` | QR image is left-aligned; feels lopsided | `items-start` → `items-center` on flex container |

**Out of scope for this prompt:**
- Studio logo/avatar upload — requires new backend endpoint for file upload
- Studio bio/description, business hours, contact fields — new backend fields needed
- Social media links — no backend model for these
- Section tabs / anchor navigation — significant layout restructure; addressed in a future prompt
- Leaflet map keyboard accessibility — Leaflet's own limitation; requires custom fork or plugin
- "Last saved" timestamp — no `updatedAt` field in `StudioResponse`

---

## Part 1 — `StudioProfilePage.tsx`

### 1a — Fix page title to match nav

The OwnerLayout nav label is `"Studio Settings"`. The page header says `"Studio Profile"`.
Change the header `<span>` to match:

```tsx
<span className="font-semibold tracking-tight">Studio Settings</span>
```

### 1b — Widen container

Change `max-w-lg` to `max-w-2xl`:

```tsx
<main className="max-w-2xl mx-auto px-4 py-6 space-y-4">
```

### 1c — Replace loading spinner with `StudioProfileSkeleton`

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
        {/* Slug info card */}
        <div className="rounded-xl border bg-card p-3">
          <Skeleton className="h-4 w-64" />
        </div>
        {/* Studio details card */}
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

Add `Skeleton` to the shadcn imports:

```tsx
import { Skeleton } from "@/shared/components/ui/skeleton";
```

Replace the loading branch:

```tsx
if (isLoading) {
  return <StudioProfileSkeleton />;
}
```

### 1d — Add "Studio URL:" label to the slug card

Replace the slug card:

```tsx
{studio && (
  <Card>
    <CardContent className="py-3 px-4 flex items-center gap-2 text-sm text-muted-foreground flex-wrap">
      <span className="text-xs font-medium text-foreground">Studio URL:</span>
      <span className="font-mono text-xs">{studio.slug}</span>
      <span className="text-muted-foreground/50">·</span>
      <span>Registered {new Date(studio.createdAt).toLocaleDateString("en-GB")}</span>
    </CardContent>
  </Card>
)}
```

### 1e — Add map helper text

Inside the Studio details form, below the `<Label>Location</Label>` line and above the
`<LocationPicker>`, add:

```tsx
<p className="text-xs text-muted-foreground">
  Click the map or drag the pin to update your studio location.
</p>
```

---

## Part 2 — `NotificationPreferencesCard.tsx`

### 2a — Fix "Sms" → "SMS" column header

Add a display label mapping after the `CHANNELS` constant:

```ts
const CHANNEL_LABELS: Record<NotificationChannel, string> = {
  Email: "Email",
  Sms:   "SMS",
};
```

In the table `<thead>`, change:

```tsx
// BEFORE
{CHANNELS.map((ch) => (
  <th key={ch} ...>{ch}</th>
))}

// AFTER
{CHANNELS.map((ch) => (
  <th key={ch} ...>{CHANNEL_LABELS[ch]}</th>
))}
```

### 2b — Add accessible names to each `ToggleSwitch`

Update the `ToggleSwitch` props interface to accept `aria-label`:

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
      // ... rest of className unchanged ...
    >
      {/* span unchanged */}
    </button>
  );
}
```

In the table body, pass the label:

```tsx
<ToggleSwitch
  checked={local[prefKey(type, channel)] ?? true}
  onChange={() => toggle(type, channel)}
  aria-label={`${label} via ${CHANNEL_LABELS[channel]}`}
/>
```

### 2c — Make "Save preferences" full-width

Change the `<Button>`:

```tsx
<Button
  size="sm"
  className="w-full gap-2"      // ← add w-full
  onClick={handleSave}
  disabled={saving || !dirty || isLoading}
>
```

---

## Part 3 — `BrandingSettingsCard.tsx`

### 3a — Check for shadcn/ui Switch component

Before making changes, check if `src/shared/components/ui/switch.tsx` exists.

**If it exists:** import it and use it directly.
**If it does NOT exist:** check the shadcn/ui documentation pattern and create `switch.tsx`
following the same convention as other components in `src/shared/components/ui/` — OR use
the same approach as `NotificationPreferencesCard`'s hand-rolled `ToggleSwitch` (extract it to
a shared file at `src/shared/components/ui/toggle-switch.tsx` and import from there in both
`BrandingSettingsCard` and `NotificationPreferencesCard`).

The goal is a **single toggle/switch implementation** used in both cards, consistent in visual style.

### 3b — Replace Badge + Button with a single Switch

The current card has:
- A `<Badge>` showing "On" or "Off" — static display only
- A `<Button>` "Disable branding" / "Enable branding" — the actual control

The audit sees this as two controls with an unclear relationship. Replace with a single Switch:

```tsx
import { Loader2 }  from "lucide-react";
import { toast }    from "sonner";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Switch }   from "@/shared/components/ui/switch";  // adjust path if needed
import { useGetMyStudioQuery, useUpdateStudioBrandingMutation } from "../studiosApi";

export function BrandingSettingsCard() {
  const { data: studio } = useGetMyStudioQuery();
  const [updateBranding, { isLoading }] = useUpdateStudioBrandingMutation();

  if (!studio) return null;

  const canToggleOff  = studio.allowBrandingRemoval;
  const isDisabled    = isLoading || (!canToggleOff && studio.showPlatformBranding);
  const upgradeHint   = !canToggleOff && studio.showPlatformBranding
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
        err && typeof err === "object" && "data" in err && err.data &&
        typeof err.data === "object" && "message" in err.data
          ? String((err.data as { message: string }).message)
          : "Upgrade to remove branding.";
      toast.error(message);
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Platform branding</CardTitle>
      </CardHeader>
      <CardContent>
        <div
          className="flex items-center justify-between gap-4"
          title={upgradeHint}
        >
          <div className="space-y-0.5">
            <p className="text-sm font-medium">
              Show "Powered by Pena e Artë" on booking widget
            </p>
            <p className="text-xs text-muted-foreground">
              Displayed in the booking widget footer for your clients.
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

**Note:** If `Switch` from shadcn/ui is not available and you use the hand-rolled `ToggleSwitch`,
replace `<Switch checked=... onCheckedChange=... ...>` with:
```tsx
<ToggleSwitch
  checked={studio.showPlatformBranding}
  onChange={handleToggle}
  disabled={isDisabled}
  aria-label="Show platform branding on booking widget"
/>
```
and update `ToggleSwitch` to accept a `disabled` prop and apply `opacity-50 cursor-not-allowed`
when disabled.

---

## Part 4 — `ReferralCodeCard.tsx`

Fix the "Studios referred" plural-always bug on lines 103–104:

```tsx
// BEFORE
<p className="text-xs text-muted-foreground">Studios referred</p>

// AFTER
<p className="text-xs text-muted-foreground">
  Studio{stats.redemptionCount !== 1 ? "s" : ""} referred
</p>
```

---

## Part 5 — `EmbedCodeCard.tsx`

### 5a — Use `VITE_PUBLIC_URL` env var for embed base URL

The booking widget embed URL should use the public-facing platform URL, not the admin panel origin.
In production these may differ (admin on `app.penaearte.com`, public booking on `penaearte.com`).

Change the URL derivation:

```tsx
// BEFORE
const embedUrl = `${window.location.origin}/embed/${studio.slug}`;

// AFTER
const EMBED_BASE = import.meta.env.VITE_PUBLIC_URL ?? window.location.origin;
const embedUrl   = `${EMBED_BASE}/embed/${studio.slug}`;
```

The fallback to `window.location.origin` means existing behaviour is preserved in environments
where `VITE_PUBLIC_URL` is not set (e.g. local dev). No `.env` files need to be changed now;
the environment variable is read at runtime.

### 5b — Add HTML comment to snippet

Add a comment about the width/height params so that users know they can customize the embed:

```tsx
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

---

## Part 6 — `QrCodeSection.tsx`

Center the QR image and download button. On line 48, change:

```tsx
// BEFORE
<div className="flex flex-col items-start gap-4">

// AFTER
<div className="flex flex-col items-center gap-4">
```

No test changes needed — `QrCodeSection.test.tsx` tests the image presence and alt text, not
its alignment.

---

## Part 7 — `StudioProfilePage.test.tsx`

### 7a — One test to UPDATE

```ts
// BEFORE
describe("StudioProfilePage — loading state", () => {
  it("shows a loading spinner while studio data is being fetched", () => {
    renderPage();
    expect(screen.getByText(/loading/i)).toBeInTheDocument();
  });
});

// AFTER
describe("StudioProfilePage — loading state", () => {
  it("shows a skeleton loading state while studio data is being fetched", () => {
    renderPage();
    expect(screen.getByLabelText("Loading studio settings")).toBeInTheDocument();
  });
});
```

### 7b — New tests to append inside `describe("StudioProfilePage — after data loads", ...)`

```ts
it("shows 'Studio Settings' as the page header title", async () => {
  renderPage();
  await waitForForm();
  expect(screen.getByText("Studio Settings")).toBeInTheDocument();
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
    screen.getByText(/click the map or drag the pin/i),
  ).toBeInTheDocument();
});
```

---

## Part 8 — Create `BrandingSettingsCard.test.tsx`

Create `frontend/src/features/studios/__tests__/BrandingSettingsCard.test.tsx`:

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

const STUDIO_BRANDING_ON: StudioResponse = {
  id:                   "studio-001",
  name:                 "Ink & Soul Studio",
  slug:                 "ink-soul-studio",
  city:                 "Lisbon",
  latitude:             38.7169,
  longitude:            -9.1395,
  showPlatformBranding: true,
  allowBrandingRemoval: false,     // plan does NOT allow removal
  trialExpiresAt:       "2099-01-01T00:00:00Z",
  createdAt:            "2025-01-01T00:00:00Z",
  isActive:             true,
};

const STUDIO_BRANDING_REMOVABLE: StudioResponse = {
  ...STUDIO_BRANDING_ON,
  showPlatformBranding: true,
  allowBrandingRemoval: true,      // plan allows removal
};

const STUDIO_BRANDING_OFF: StudioResponse = {
  ...STUDIO_BRANDING_REMOVABLE,
  showPlatformBranding: false,
  allowBrandingRemoval: true,
};

// Use the StudioResponse type from studiosApi — import it if exported, otherwise inline:
type StudioResponse = (typeof STUDIO_BRANDING_ON);

// ── MSW server ────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me", () =>
    HttpResponse.json(STUDIO_BRANDING_ON),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ───────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                     authReducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(studiosApi.middleware),
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

  it("renders the card title", async () => {
    renderCard();
    expect(await screen.findByText(/platform branding/i)).toBeInTheDocument();
  });

  it("shows the branding description text", async () => {
    renderCard();
    expect(await screen.findByText(/powered by pena e artt?ë/i)).toBeInTheDocument();
  });

  it("switch is checked when showPlatformBranding is true", async () => {
    renderCard();
    const sw = await screen.findByRole("switch");
    expect(sw).toHaveAttribute("aria-checked", "true");
  });

  it("switch is unchecked when showPlatformBranding is false", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/me", () =>
        HttpResponse.json(STUDIO_BRANDING_OFF),
      ),
    );
    renderCard();
    const sw = await screen.findByRole("switch");
    expect(sw).toHaveAttribute("aria-checked", "false");
  });

  it("switch is disabled when plan does not allow branding removal and branding is on", async () => {
    // Default: STUDIO_BRANDING_ON with allowBrandingRemoval = false
    renderCard();
    const sw = await screen.findByRole("switch");
    expect(sw).toBeDisabled();
  });

  it("switch is enabled when plan allows branding removal", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/me", () =>
        HttpResponse.json(STUDIO_BRANDING_REMOVABLE),
      ),
    );
    renderCard();
    const sw = await screen.findByRole("switch");
    expect(sw).not.toBeDisabled();
  });

  it("shows upgrade hint text when plan does not allow branding removal", async () => {
    renderCard();
    expect(await screen.findByText(/upgrade your plan/i)).toBeInTheDocument();
  });

  it("does NOT show upgrade hint when plan allows removal", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/me", () =>
        HttpResponse.json(STUDIO_BRANDING_REMOVABLE),
      ),
    );
    renderCard();
    await screen.findByRole("switch");
    expect(screen.queryByText(/upgrade your plan/i)).not.toBeInTheDocument();
  });

  it("calls updateBranding with inverted showPlatformBranding when switch is clicked", async () => {
    const updateSpy = vi.fn();
    server.use(
      http.get("http://localhost/api/v1/studios/me", () =>
        HttpResponse.json(STUDIO_BRANDING_REMOVABLE),
      ),
      http.patch("http://localhost/api/v1/studios/studio-001/branding", async ({ request }) => {
        const body = await request.json();
        updateSpy(body);
        return HttpResponse.json({ ...STUDIO_BRANDING_REMOVABLE, showPlatformBranding: false });
      }),
    );

    const user = userEvent.setup();
    renderCard();
    const sw = await screen.findByRole("switch");
    await user.click(sw);

    await waitFor(() => expect(updateSpy).toHaveBeenCalledOnce());
    expect(updateSpy).toHaveBeenCalledWith(
      expect.objectContaining({ showPlatformBranding: false }),
    );
  });

});
```

> **Note:** The MSW handler for `updateBranding` uses `PATCH`. Check `studiosApi.ts` to confirm
> the method and URL pattern used by `useUpdateStudioBrandingMutation` and update the test handler
> to match exactly. The test asserts `showPlatformBranding: false` because `STUDIO_BRANDING_REMOVABLE`
> starts with `showPlatformBranding: true`.

---

## Part 9 — Verify

```bash
# TypeScript — must be clean (no new errors beyond pre-existing)
pnpm tsc --noEmit

# Studio tests — all must pass (3 updated/new in StudioProfilePage + new BrandingSettingsCard tests)
pnpm test src/features/studios --run

# Notifications (Sms → SMS, aria-label changes)
pnpm test src/features/notifications --run

# Full suite smoke-check
pnpm test --run
```

---

## Reference: What the Audit Misread (Do NOT re-implement)

| Audit claim | Reality — already correct |
|---|---|
| "Embed URL hardcoded to localhost" | Uses `window.location.origin` — not hardcoded; Part 5 improves it further |
| "Referral code hidden behind Generate button" | Code is shown in a copyable field when it exists; `Generate referral code` button only shown when no code exists |
| "'Disable branding' doesn't describe state" | Button already shows "Disable branding" OR "Enable branding" based on `showPlatformBranding` |
| "QR code has no alt text" | Alt text: `\`QR code for ${studio.name}\`` already present |

---

## Constraints (from CLAUDE.md)

- Do NOT add new npm packages. `Switch` must come from shadcn/ui (already in the project) or
  be the extracted hand-rolled `ToggleSwitch` — no new library.
- No useEffect for data fetching. The `useEffect` in `NotificationPreferencesCard` that syncs
  local state from fetched data is acceptable (state sync, not a fetch).
- TypeScript strict mode — no `any`, explicit types everywhere.
- No default exports on components.
- Logs must include `tenant_id`, `user_id`, `request_id` — but all changes here are frontend only,
  so this rule is not exercised.
