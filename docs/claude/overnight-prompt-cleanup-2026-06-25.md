# Overnight Prompt — E2E Fix, EmbedPage, Repository Hygiene
**Date:** 2026-06-25
**Scope:** Three independent tasks: fix the failing E2E critical-path suite,
            complete/document `EmbedPage.tsx`, and clean up untracked files from
            the repository.

---

## Context

Read `CLAUDE.md` before starting. No new npm or NuGet packages.
TypeScript strict mode — no `any`, no default exports on components.

---

## Task A — Fix `frontend/e2e/critical-path.spec.ts`

### A1 — Understand before touching

**Read these files first:**
```
frontend/src/features/auth/components/RegisterStudioPage.tsx
frontend/src/features/booking/components/BookingWidget.tsx   (if exists)
frontend/src/features/auth/components/LoginPage.tsx
frontend/src/features/appointments/components/BookAppointmentForm.tsx
```

Then **run the test suite** to see actual failure messages:
```bash
cd frontend
pnpm exec playwright test e2e/critical-path.spec.ts --reporter=list 2>&1
```

If the dev server isn't running, Playwright's `webServer` block starts it automatically.
If startup fails, run `pnpm dev` in one terminal and add
`PLAYWRIGHT_BASE_URL=http://localhost:5173` before the playwright command.

Capture the full error output. Let it guide all fixes in this task.

---

### A2 — Known issues to address regardless of which tests fail

Apply these even if the tests currently pass — they are correctness fixes:

#### A2a — Add `slugLockedAt` to every `StudioResponse` mock

`StudioResponse` was extended in a prior session to include `DateTime? SlugLockedAt`.
The frontend interface now has `slugLockedAt: string | null`. Every mock in
`critical-path.spec.ts` that returns a `StudioResponse`-shaped object must include
this field or the typed interface will diverge from the mock.

In `mockStudioRegistration` (the POST `/api/v1/studios` response) add:
```ts
slugLockedAt: null,
```

In `mockStudioMe` (the GET `/api/v1/studios/me` response) add:
```ts
slugLockedAt: null,
```

#### A2b — Radix Select: add explicit wait for portal options

`page.getByLabel("Artist").click()` opens the Radix `<Select>` dropdown. The options
render in a React portal — Playwright must wait for them to be visible before clicking.
Replace the two-step select in the "client creates an appointment" test:

```ts
// Before (may miss the portal):
await page.getByLabel("Artist").click();
await page.getByRole("option", { name: "Rafaela Costa" }).click();

// After:
await page.getByLabel("Artist").click();
await page.getByRole("option", { name: "Rafaela Costa" })
  .waitFor({ state: "visible", timeout: 5_000 });
await page.getByRole("option", { name: "Rafaela Costa" }).click();
```

#### A2c — Registration flow: confirm how the app navigates after registering

Read `RegisterStudioPage.tsx`. Answer: after a successful `/auth/register` call, does
the component:
(a) receive a token in the response and dispatch `setCredentials`, OR
(b) redirect to `/login` and let the user log in manually?

If (a): the mock for `/auth/register` must return `{ accessToken: OWNER_TOKEN, tokenType: "Bearer" }` (same shape as `/auth/login`). The current mock returns `{}`. Fix it:
```ts
await page.route("**/api/v1/auth/register", async (route) => {
  await route.fulfill({
    status: 200,
    contentType: "application/json",
    body: JSON.stringify({ accessToken: OWNER_TOKEN, tokenType: "Bearer" }),
  });
});
```

If (b): after clicking "Register" the page should navigate to `/login`. Then the test
must fill the login form and click "Sign in". In that case, fix the test's assertions —
after `page.getByRole("button", { name: "Register" }).click()`, wait for `/login`, then
fill credentials and submit.

Implement whichever branch matches the actual `RegisterStudioPage` logic. Do not guess.

#### A2d — Confirm `BookingWidget` makes no unmocked calls

If `BookingWidget.tsx` exists, read it. If it issues any API calls (RTK Query or
otherwise) beyond what `mockBookingApis` already mocks, add the missing
`page.route(...)` intercepts in `mockBookingApis`. Common culprits:
- `GET /api/v1/studios/me` — already mocked by `mockStudioMe`; verify the pattern
  `**/api/v1/studios/me` matches the actual URL shape
- Any other query

#### A2e — `scheduledAt` value must be strictly in the future

The `scheduledAt` Zod schema refines `new Date(v) > new Date()`. The test computes:
```ts
const future = new Date(Date.now() + 7 * 86_400_000);
const dateStr = future.toISOString().slice(0, 16); // "YYYY-MM-DDTHH:MM"
```

`datetime-local` inputs interpret the value as **local time**, but
`toISOString()` produces UTC. In timezones behind UTC this can produce
a date that the browser treats as past. Fix:

```ts
// Compute the local datetime string directly:
const future = new Date(Date.now() + 7 * 86_400_000);
const pad = (n: number) => String(n).padStart(2, "0");
const dateStr =
  `${future.getFullYear()}-${pad(future.getMonth() + 1)}-${pad(future.getDate())}` +
  `T${pad(future.getHours())}:${pad(future.getMinutes())}`;
```

---

### A3 — After all fixes: run the suite and confirm green

```bash
pnpm exec playwright test e2e/critical-path.spec.ts --reporter=list 2>&1
```

All three tests must pass. If any still fails, read the Playwright trace
(`playwright-report/` or the html report at `playwright-report/index.html`) and fix.

---

## Task B — `EmbedPage.tsx` improvements

**File:** `frontend/src/features/public/components/EmbedPage.tsx`

`EmbedPage` at `/embed/:studioSlug` is a fully functional booking widget designed to
be embedded in studio websites via an `<iframe>` tag (see `EmbedCodeCard.tsx` which
generates the snippet). Two issues need fixing and the component should be documented.

### B1 — Fix the studio page URL (production bug)

The current code:
```ts
const studioPageUrl = `${window.location.origin}/s/${studio.slug}`;
```

When `EmbedPage` runs inside an iframe on a third-party website, `window.location.origin`
is the admin app's origin (e.g. `https://app.penaearte.com`), not the public
marketing site. The booking CTA would send visitors to the admin app instead of the
public portfolio page.

Fix — mirror the same pattern used in `EmbedCodeCard.tsx`:
```ts
const EMBED_BASE    = import.meta.env.VITE_PUBLIC_URL ?? window.location.origin;
const studioPageUrl = `${EMBED_BASE}/s/${studio.slug}`;
```

### B2 — Replace `Loader2` spinner with `Skeleton`

Loading state currently shows:
```tsx
<div className="flex items-center justify-center min-h-screen bg-background">
  <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
</div>
```

Replace with a structural skeleton that mirrors the actual layout:
```tsx
import { Skeleton } from "@/shared/components/ui/skeleton";

// Remove the Loader2 import if no longer used elsewhere in the file.

function EmbedSkeleton() {
  return (
    <div className="min-h-screen bg-background flex flex-col" aria-label="Loading booking widget">
      <Skeleton className="h-32 w-full rounded-none" />
      <div className="flex-1 px-4 py-5 space-y-4">
        <div className="space-y-2">
          <Skeleton className="h-5 w-36" />
          <Skeleton className="h-3 w-20" />
          <Skeleton className="h-3 w-full" />
        </div>
        <Skeleton className="h-10 w-full rounded-md" />
        <div className="space-y-2">
          <Skeleton className="h-3 w-16" />
          <Skeleton className="h-12 w-full rounded-md" />
          <Skeleton className="h-12 w-full rounded-md" />
        </div>
      </div>
    </div>
  );
}
```

Replace the loading branch:
```tsx
if (isLoading) return <EmbedSkeleton />;
```

### B3 — Add `aria-label` to the error state

```tsx
if (isError || !studio) {
  return (
    <div
      className="flex items-center justify-center min-h-screen bg-background"
      role="alert"
      aria-live="polite"
    >
      <p className="text-sm text-muted-foreground">Studio not found.</p>
    </div>
  );
}
```

### B4 — Document `EmbedPage` in `architecture.md`

**File:** `docs/claude/architecture.md`

Find the "Feature Module Map" section. Add `EmbedPage` to the `public` feature entry.
The entry should state:

```
/embed/:studioSlug  EmbedPage     Booking widget for embedding via <iframe> on studio
                                  websites. Served from VITE_PUBLIC_URL domain. Uses
                                  AllowAnonymous. No auth, no Redux — reads public studio
                                  data only. Generated snippet lives in EmbedCodeCard.tsx.
```

---

## Task C — Repository hygiene

### C1 — Add entries to `.gitignore`

**File:** `.gitignore` (the root-level one)

Append the following block at the end of the file:

```gitignore

## Verification / probe artefacts (generated by overnight verification scripts)
Temppena_verify/
verify_overnight.cjs
verify_overnight.mjs
verify_*.mjs
verify_*.cjs
debug_*.mjs
probe_*.mjs

## Crash dumps
*.stackdump

## Archives and binaries checked in by mistake
files.zip
```

### C2 — Delete the crash dump

**File:** `grep.exe.stackdump` (in the project root)

Delete this file. It is a Windows crash dump from a previous `grep` run and has no
value in the repository. Use shell:
```bash
rm "/sessions/cool-exciting-sagan/mnt/Pena_e_Artë_AIO_Studio/Pena e Arte/grep.exe.stackdump"
```

> If `Temppena_verify/` contains anything hand-crafted (check the listing — it contains
> screenshots like `01-client-list.png`, JS probe scripts, and build output DLLs), do NOT
> delete it yet — the `.gitignore` entry is enough to stop it from being tracked.
> Flag in a comment if it should be deleted.

### C3 — Commit the docs/claude overnight prompt files

The files listed below are legitimate documentation and should be tracked in git:
```
docs/claude/overnight-prompt-studio-settings-2026-06-19.md
docs/claude/overnight-prompt-studio-settings-v2-2026-06-19.md
docs/claude/overnight-prompt-ui-polish-2026-06-19.md
docs/claude/overnight-prompt-discovery-reviews-2026-06-25.md
docs/claude/overnight-prompt-cleanup-2026-06-25.md
```

Stage them:
```bash
cd "/sessions/cool-exciting-sagan/mnt/Pena_e_Artë_AIO_Studio/Pena e Arte"
git add docs/claude/overnight-prompt-*.md
git add .gitignore
git rm --cached grep.exe.stackdump 2>/dev/null || true
git status
```

Do NOT commit yet — just stage. The commit message and actual commit is left to the
developer to review and sign off.

---

## Task D — EmbedPage: write a test

**File:** `frontend/src/features/public/__tests__/EmbedPage.test.tsx` (create new)

Read `StudioPortfolioPage.test.tsx` and `ArtistPortfolioPage.test.tsx` first to match
the established mock and render pattern for public pages.

Write three tests:

1. **`Shows skeleton while loading`** — mock `useGetPublicStudioQuery` as loading,
   assert `aria-label="Loading booking widget"` is present.

2. **`Shows error state when studio not found`** — mock query returns `isError: true`,
   assert "Studio not found." text is visible.

3. **`Renders studio name, book button, and artist list`** — mock query returns a
   `PublicStudioResponse` with `showBookingCta: true` and two artists. Assert:
   - studio name is in the document
   - "Book an Appointment" button is present
   - both artist names are listed

---

## Verification checklist

After all tasks:

- [ ] `pnpm exec playwright test e2e/critical-path.spec.ts --reporter=list` — all three tests pass.
- [ ] `pnpm build` — zero TypeScript errors.
- [ ] `pnpm lint` — zero lint errors.
- [ ] `pnpm test` — unit tests green (EmbedPage.test.tsx included).
- [ ] `git status` — `Temppena_verify/` and `grep.exe.stackdump` are NOT listed as untracked.
- [ ] `git status` — `docs/claude/overnight-prompt-*.md` files are staged.
- [ ] `/embed/tinta-alma` renders the studio name and a skeleton during load (manual spot-check in browser).
- [ ] `VITE_PUBLIC_URL` in the embed URL now uses the env var if set (verify the compiled output).

---

## Summary of files changed

```
frontend/e2e/critical-path.spec.ts                               modified
frontend/src/features/public/components/EmbedPage.tsx            modified
frontend/src/features/public/__tests__/EmbedPage.test.tsx        created
docs/claude/architecture.md                                       modified (Feature Module Map)
.gitignore                                                        modified
grep.exe.stackdump                                                deleted
```
