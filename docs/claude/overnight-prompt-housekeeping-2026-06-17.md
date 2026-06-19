# Overnight Prompt — Housekeeping & E2E Setup (2026-06-17)

> **Scope:** P2 issue #36 (Playwright e2e critical-path test) + P4 housekeeping items #12–14.
>
> Four tasks. Work in order. Commit after each one.
> Do NOT introduce new npm or NuGet packages beyond `@playwright/test`.

---

## 0. Mandatory Reading (Do This First)

Before writing a single line of code, read these files:

```
CLAUDE.md
docs/claude/frontend.md
docs/claude/conventions.md
docs/claude/architecture.md
```

Also check the frontend router to understand the exact route paths:

```
frontend/src/app/router.tsx   (or wherever the React Router config lives)
```

You will need the real route paths for Task 1.

---

## 1. P2 #36 — Playwright E2E Setup

### Context

`@playwright/test` is NOT in `devDependencies` — only as a transitive dep.
No `playwright.config.ts` exists. No `e2e/` folder exists. The frontend
dev server proxies `/api` and `/hubs` to `http://localhost:5078`.

### 1a. Add `@playwright/test` as a devDependency

In `frontend/`:

```bash
pnpm add -D @playwright/test
```

Then install Chromium browser binaries:

```bash
pnpm exec playwright install --with-deps chromium
```

> `--with-deps` installs OS-level system dependencies for the browser.
> If running on a machine without sudo, use `playwright install chromium` without
> `--with-deps` and manually ensure the OS deps are in place.

### 1b. Add `test:e2e` script to `package.json`

In `frontend/package.json`, add to `"scripts"`:

```json
"test:e2e": "playwright test"
```

Keep the existing `"test": "vitest run"` unchanged.

### 1c. Create `frontend/playwright.config.ts`

```typescript
import { defineConfig, devices } from "@playwright/test";

/**
 * E2E tests run against the Vite dev server (frontend only).
 * API calls are intercepted via Playwright route mocking — the .NET backend
 * does not need to be running during the e2e suite.
 *
 * To run against a live backend, set PLAYWRIGHT_BASE_URL=http://localhost:5173
 * and ensure `dotnet run --project Pena_e_Arte.API` is running on port 5078.
 */
export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1,
  reporter: process.env.CI ? "github" : "html",
  use: {
    baseURL: process.env.PLAYWRIGHT_BASE_URL ?? "http://localhost:5173",
    trace: "on-first-retry",
    screenshot: "only-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
  webServer: {
    command: "pnpm dev",
    url: "http://localhost:5173",
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
    stdout: "pipe",
    stderr: "pipe",
  },
});
```

### 1d. Create `frontend/e2e/critical-path.spec.ts`

This test covers the most important user journey: studio registers, logs in,
and creates an appointment. All API calls are mocked via Playwright's
`page.route()` so the backend does not need to be running.

Before writing the test, read the router config (found in step 0) to confirm:
- The path for the studio registration page
- The path for the login page
- The path for creating an appointment
- The form field names / `data-testid` attributes in use

If the components do NOT have `data-testid` attributes, use ARIA roles and
label text to locate elements — Playwright's `getByRole`, `getByLabel`,
`getByPlaceholder`. Do not add `data-testid` attributes to production
components unless they are already present.

Here is the test skeleton — **fill in the correct selectors and routes**
from your inspection of the actual source files:

```typescript
import { test, expect, type Page } from "@playwright/test";

// ---------------------------------------------------------------------------
// Shared mock setup
// ---------------------------------------------------------------------------

const STUDIO_ID    = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
const ARTIST_ID    = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
const CLIENT_ID    = "cccccccc-cccc-cccc-cccc-cccccccccccc";
const APPT_ID      = "dddddddd-dddd-dddd-dddd-dddddddddddd";

/**
 * JWT payload (claims only — not a real signed token).
 * The frontend reads claims from the token; it does not validate the signature
 * in the browser (that is the backend's job).
 */
const FAKE_TOKEN = (() => {
  const header  = btoa(JSON.stringify({ alg: "HS256", typ: "JWT" }));
  const payload = btoa(
    JSON.stringify({
      sub:       "user-id-owner",
      email:     "owner@tinta-alma.com",
      role:      "owner",
      tenant_id: STUDIO_ID,
      exp:       Math.floor(Date.now() / 1000) + 3600,
    }),
  );
  return `${header}.${payload}.fakesig`;
})();

async function mockAuthAndStudio(page: Page) {
  // Registration
  await page.route("**/api/auth/register", async (route) => {
    await route.fulfill({
      status:      200,
      contentType: "application/json",
      body: JSON.stringify({
        token:    FAKE_TOKEN,
        studioId: STUDIO_ID,
        name:     "Tinta & Alma",
        slug:     "tinta-alma",
      }),
    });
  });

  // Login
  await page.route("**/api/auth/login", async (route) => {
    await route.fulfill({
      status:      200,
      contentType: "application/json",
      body: JSON.stringify({
        token:    FAKE_TOKEN,
        studioId: STUDIO_ID,
      }),
    });
  });

  // Studio "me" — used by the dashboard/layout to load current studio context
  await page.route("**/api/studios/me", async (route) => {
    await route.fulfill({
      status:      200,
      contentType: "application/json",
      body: JSON.stringify({
        id:           STUDIO_ID,
        name:         "Tinta & Alma",
        slug:         "tinta-alma",
        city:         "Porto",
        isActive:     true,
        showBranding: true,
        trialExpiresAt: new Date(Date.now() + 14 * 86_400_000).toISOString(),
      }),
    });
  });

  // Subscription check (trial active)
  await page.route("**/api/subscriptions/current", async (route) => {
    await route.fulfill({
      status:      200,
      contentType: "application/json",
      body: JSON.stringify({
        status:         "trialing",
        trialExpiresAt: new Date(Date.now() + 14 * 86_400_000).toISOString(),
      }),
    });
  });
}

async function mockAppointments(page: Page) {
  // GET appointments list
  await page.route("**/api/appointments*", async (route) => {
    if (route.request().method() === "GET") {
      await route.fulfill({
        status:      200,
        contentType: "application/json",
        body: JSON.stringify([]),
      });
    } else {
      route.continue();
    }
  });

  // Artists list (needed for the appointment form artist picker)
  await page.route("**/api/artists*", async (route) => {
    await route.fulfill({
      status:      200,
      contentType: "application/json",
      body: JSON.stringify([
        { id: ARTIST_ID, firstName: "Rafaela", lastName: "Costa", email: "rafaela@tinta-alma.com" },
      ]),
    });
  });

  // Clients list (needed for the appointment form client picker)
  await page.route("**/api/clients*", async (route) => {
    await route.fulfill({
      status:      200,
      contentType: "application/json",
      body: JSON.stringify([
        { id: CLIENT_ID, firstName: "Ana", lastName: "Silva", email: "ana@example.com" },
      ]),
    });
  });

  // POST create appointment
  await page.route("**/api/appointments", async (route) => {
    if (route.request().method() === "POST") {
      await route.fulfill({
        status:      201,
        contentType: "application/json",
        body: JSON.stringify({
          id:             APPT_ID,
          clientId:       CLIENT_ID,
          artistId:       ARTIST_ID,
          date:           new Date(Date.now() + 7 * 86_400_000).toISOString(),
          durationMinutes: 120,
          status:         "Pending",
        }),
      });
    } else {
      route.continue();
    }
  });
}

// ---------------------------------------------------------------------------
// Test: register → login → create appointment (critical path)
// ---------------------------------------------------------------------------

test.describe("Critical path — register, login, create appointment", () => {

  test("owner registers a studio successfully", async ({ page }) => {
    await mockAuthAndStudio(page);

    // Navigate to the registration page
    // ↓ Replace "/register" with the actual route path from the router config
    await page.goto("/register");

    // Fill the registration form
    // ↓ Inspect RegisterStudioPage.tsx for the actual form field labels/placeholders
    await page.getByLabel(/studio name/i).fill("Tinta & Alma");
    await page.getByLabel(/city/i).fill("Porto");
    await page.getByLabel(/email/i).fill("owner@tinta-alma.com");
    await page.getByLabel(/password/i).first().fill("Password123!");
    // If there is a "confirm password" field:
    const confirmField = page.getByLabel(/confirm password/i);
    if (await confirmField.count() > 0) {
      await confirmField.fill("Password123!");
    }

    // Submit
    await page.getByRole("button", { name: /register|create studio|sign up/i }).click();

    // Assert success — the app should redirect to the owner dashboard
    // ↓ Replace "/dashboard" with the actual post-registration redirect route
    await expect(page).toHaveURL(/\/(dashboard|studio|home)/i, { timeout: 10_000 });
  });

  test("owner logs in successfully", async ({ page }) => {
    await mockAuthAndStudio(page);

    // ↓ Replace "/login" with the actual login route
    await page.goto("/login");

    await page.getByLabel(/email/i).fill("owner@tinta-alma.com");
    await page.getByLabel(/password/i).fill("Password123!");
    await page.getByRole("button", { name: /log in|sign in|login/i }).click();

    await expect(page).toHaveURL(/\/(dashboard|studio|home)/i, { timeout: 10_000 });
  });

  test("owner creates an appointment", async ({ page }) => {
    await mockAuthAndStudio(page);
    await mockAppointments(page);

    // Start at login and authenticate first
    // ↓ Replace "/login" with the actual login route
    await page.goto("/login");
    await page.getByLabel(/email/i).fill("owner@tinta-alma.com");
    await page.getByLabel(/password/i).fill("Password123!");
    await page.getByRole("button", { name: /log in|sign in|login/i }).click();

    // Navigate to the appointments page or new appointment form
    // ↓ Check the router for the exact path (e.g. "/appointments/new" or "/appointments")
    await page.goto("/appointments/new");

    // Fill the appointment form
    // ↓ Inspect the CreateAppointmentPage / AppointmentForm component for exact field labels
    // Common fields: client picker, artist picker, date, duration, notes
    const clientSelect = page.getByLabel(/client/i);
    if (await clientSelect.count() > 0) {
      await clientSelect.selectOption({ label: "Ana Silva" });
    }

    const artistSelect = page.getByLabel(/artist/i);
    if (await artistSelect.count() > 0) {
      await artistSelect.selectOption({ label: "Rafaela Costa" });
    }

    // Date — pick a date 7 days from now (format depends on the input type)
    const future = new Date(Date.now() + 7 * 86_400_000);
    const dateStr = future.toISOString().slice(0, 16); // "YYYY-MM-DDTHH:MM"
    const dateInput = page.getByLabel(/date/i);
    if (await dateInput.count() > 0) {
      await dateInput.fill(dateStr);
    }

    const durationInput = page.getByLabel(/duration/i);
    if (await durationInput.count() > 0) {
      await durationInput.fill("120");
    }

    // Submit
    await page.getByRole("button", { name: /create|book|save|schedule/i }).click();

    // Assert success — either redirect to appointments list or a success toast
    // ↓ Adjust selector based on what the component actually renders on success
    await expect(
      page.getByText(/appointment created|booked|scheduled/i)
        .or(page.getByRole("link", { name: /appointments/i }))
    ).toBeVisible({ timeout: 10_000 });
  });

});
```

> **Important:** After writing the skeleton above, read the actual component
> source files for `RegisterStudioPage.tsx`, `LoginPage.tsx`, and the
> appointment creation component. Replace all `↓ Replace...` comments with
> the correct values. If any selector fails (element not found), inspect the
> component and use the appropriate Playwright locator. Do not leave any
> placeholder comments in the final file.

### 1e. Add `e2e/` to `.gitignore` exclusions for Playwright output

Open `.gitignore` at the project root (or `frontend/.gitignore`) and add:

```
# Playwright
frontend/test-results/
frontend/playwright-report/
frontend/.playwright-cache/
```

If there is no `frontend/.gitignore`, add these lines to the root `.gitignore`.

### 1f. Run the E2E tests

From `frontend/`:

```bash
pnpm exec playwright install chromium
pnpm test:e2e
```

All three tests must pass. If a test fails due to a wrong selector, fix the
selector by reading the actual component source — do not remove the test or
skip it. A passing but vacuous test (no real assertions) is worse than a
failing test.

**Commit:** `test(e2e): add Playwright setup + critical-path register/login/appointment test`

---

## 2. P4 #12 — Create `docs/issues.md` and Mark #33–35 Resolved

`docs/issues.md` does not exist yet. Create it as the canonical issue-tracking
reference for this project, pre-populated with the historical issues and their
current resolution status.

**File:** `docs/issues.md`

```markdown
# Issues & Known Gaps

This file tracks known issues, gaps in test coverage, and deferred improvements.
Issues are grouped by priority tier (P1–P7) and updated as they are resolved.

---

## P1 — Blocking / Security

_No open issues._

---

## P2 — Test Coverage & Quality

| # | Description | Status |
|---|---|---|
| 36 | No Playwright / Cypress e2e setup. | ✅ Resolved 2026-06-17 — `@playwright/test` added, `playwright.config.ts` created, critical-path test (`register → login → create appointment`) added to `frontend/e2e/`. |

---

## P3 — Feature Gaps

_No open issues. See `docs/claude/self-promotion-prompts.md` for planned features._

---

## P4 — Housekeeping

| # | Description | Status |
|---|---|---|
| 12 | `issues.md` tracking doc missing. | ✅ Resolved 2026-06-17 — this file created. |
| 13 | Placeholder test stubs `UnitTest1.cs` left over from project creation. | ✅ Resolved 2026-06-17 — deleted from both `UnitTests` and `IntegrationTests` projects. |
| 14 | Discrepancy between SP-02 spec (`IsPublished`) and implementation (`IsActive`) for public portfolio filtering. | ✅ Resolved 2026-06-17 — documented in `docs/claude/architecture.md` and as XML doc on `Studio.IsActive`. |

---

## P5 — Performance / Observability

_No open issues._

---

## P6 — DevOps / Infrastructure

_No open issues._

---

## P7 — Superseded / Obsolete

| # | Description | Status |
|---|---|---|
| 33 | ~3% frontend test coverage (only 28 tests at the time). | ✅ Obsolete — 908 tests across 67 files as of 2026-06-17. |
| 34 | No auth flow tests. | ✅ Obsolete — `LoginPage.test.tsx`, `authSlice.test.ts`, `RegisterStudioPage.test.tsx`, `ForgotPasswordPage.test.tsx`, `ResetPasswordPage.test.tsx` all exist. |
| 35 | No RTK Query endpoint tests. | ✅ Partially resolved — all major API slices exercised through component tests. Dedicated contract tests deemed unnecessary given the component-level coverage. Judgment call recorded here. |

---

## Adding New Issues

Use the next sequential number. Assign priority P1–P7:

- **P1** Blocking, security, data-loss
- **P2** Test coverage / quality gates
- **P3** Incomplete feature implementation
- **P4** Low-effort housekeeping
- **P5** Performance or observability gap
- **P6** DevOps / infra / deployment
- **P7** Superseded, obsolete, or recorded-for-posterity only
```

**Commit:** `docs: create issues.md with historical issue tracking and P7 #33-35 marked resolved`

---

## 3. P4 #13 — Delete Placeholder Test Stubs

Delete these two auto-generated files:

```
tests/Pena_e_Arte.UnitTests/UnitTest1.cs
tests/Pena_e_Arte.IntegrationTests/UnitTest1.cs
```

Both contain only:

```csharp
[Fact]
public void Test1()
{
}
```

They were generated by `dotnet new xunit` and have no value.

After deleting, run:

```bash
dotnet test
```

All tests must pass (no test was actually testing anything in those stubs,
so count should stay the same or go down by 2 empty tests — both are fine).

**Commit:** `chore: delete auto-generated UnitTest1.cs stubs from both test projects`

---

## 4. P4 #14 — Document `IsActive` vs `IsPublished` in Architecture

### Background

The SP-02 self-promotion spec described filtering the public studio list on
`s.IsPublished`. The `Studio` entity has no `IsPublished` field. The actual
implementation in `GetPublicStudioQuery` uses `s.IsActive`, which is
functionally correct (an inactive studio will not appear on the public
portfolio). The discrepancy is between a design doc and the implementation —
the implementation is intentional and correct.

This needs to be documented in two places so future maintainers do not:
- Try to add an `IsPublished` field
- Wonder whether the filtering is a bug

### 4a. Update `docs/claude/architecture.md`

Find the **Studio Map** section (around the heading `## Studio Map`) and
update it. Add a new paragraph immediately after the section, or insert it
into the existing `Returns only published/active studios` line:

```markdown
### IsActive vs IsPublished — intentional design decision

The SP-02 spec referred to an `IsPublished` boolean on `Studio`. No such
field exists or is planned. The public portfolio endpoints (`GetPublicStudioQuery`,
`GetPublicArtistQuery`) and the studio map endpoint filter on `Studio.IsActive`
instead.

This is **intentional**: `IsActive` already covers the intended behaviour —
deactivated studios (suspended, manually disabled by issuer) do not appear in
public-facing endpoints. A separate `IsPublished` field would add complexity
without adding expressive power given the current subscription and trial model.

If a future feature requires a studio to be active but unlisted (e.g. soft-launch
mode), add `IsPublished bool` to `Studio` at that time and update this section.
Until then, do not add `IsPublished` to the entity or the EF Core config.
```

Also add a row to the **Decisions Log** table at the bottom of `architecture.md`:

```markdown
| `IsPublished` vs `IsActive` on `Studio` | Use `IsActive` only | `IsPublished` was in the SP-02 spec but never implemented. `IsActive` covers the same use case. Adding a second flag would create redundant state. |
```

### 4b. Add XML doc comment to `Studio.IsActive`

**File:** `Pena_e_Arte.Domain/Entities/Studio.cs`

Find the `IsActive` property and add (or replace any existing) XML doc:

```csharp
/// <summary>
/// Controls whether this studio is visible in public-facing endpoints
/// (public portfolio, studio map) and whether tenant access is permitted.
/// <para>
/// NOTE: The SP-02 spec referenced an <c>IsPublished</c> field. No such field
/// exists. <c>IsActive</c> serves the same purpose — a studio that should not
/// appear publicly is simply deactivated by the issuer. Do not add
/// <c>IsPublished</c> without first updating <c>docs/claude/architecture.md</c>.
/// </para>
/// </summary>
public bool IsActive { get; set; } = true;
```

Run `dotnet build` — must succeed.

**Commit:** `docs: document IsActive vs IsPublished decision in architecture.md and Studio entity`

---

## 5. Final Verification

1. `pnpm --dir frontend test` — all Vitest unit tests pass (no regressions).
2. `pnpm --dir frontend test:e2e` — all three Playwright tests pass.
3. `dotnet test` — all .NET unit and integration tests pass.
4. `dotnet build` — zero errors, zero warnings introduced by this session.
5. `pnpm --dir frontend lint` — zero lint errors.
6. Confirm `docs/issues.md` exists and all four tasks are marked resolved in it.
7. Confirm `tests/Pena_e_Arte.UnitTests/UnitTest1.cs` is deleted.
8. Confirm `tests/Pena_e_Arte.IntegrationTests/UnitTest1.cs` is deleted.
9. Confirm `docs/claude/architecture.md` has both the `IsPublished` prose block
   and the Decisions Log row.
10. Confirm `Studio.IsActive` has the XML doc comment.
11. `git log --oneline -10` — four commits from this session are present.

---

## If You Get Stuck

- **Playwright selectors fail:** Read the component source — do NOT guess.
  Use `getByRole` and `getByLabel` over `getByTestId` unless the component
  already has `data-testid` attributes. If an element has no accessible label,
  check the JSX for `aria-label`, `placeholder`, or surrounding `<label>` tags.

- **E2E test fails because the frontend redirects after mock login:**
  The frontend reads the JWT from `localStorage`/Redux store after the
  auth endpoint returns. If the app does not redirect automatically, manually
  call `page.goto("/dashboard")` (or the correct route) after the login mock.
  Check `authSlice.ts` for what happens after a successful login response.

- **`dotnet build` fails after the `Studio.IsActive` XML doc edit:**
  Malformed XML comment syntax. The `<para>` tag and `<c>` tag must be
  closed — copy the snippet above exactly and verify angle brackets.

- **`playwright install` fails without sudo:** Drop `--with-deps` and use
  `playwright install chromium` only. Update the `test:e2e` section of
  `docs/issues.md` to note the manual OS dep requirement.
