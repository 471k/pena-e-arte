# verifier-gui

Playwright-based verifier for the React frontend. Use this skill whenever
`/verify` is run against a frontend change in this project.

## Prerequisites

- Playwright v1.60+ installed globally (`playwright` on PATH — confirmed at `C:\nvm4w\nodejs\playwright.cmd`)
- Vite dev server running on port 5173 (see `run-frontend` skill)
- Node 18+ (for `Buffer.from(...).toString("base64url")`)

## Auth bypass

The app reads `localStorage["auth_token"]` on startup and calls `jwt-decode`
(no signature verification). Inject a fake unsigned JWT before navigating to
any protected route. The helper is at `.claude/skills/verifier-gui/fake-jwt.mjs`:

```js
import { makeFakeJwt } from "./.claude/skills/verifier-gui/fake-jwt.mjs";

// artist role (ArtistAndAbove routes: /designs, /artists, /clients, /schedule)
const artistToken = makeFakeJwt({ role: "artist" });

// owner role (OwnerAndAbove routes: /dashboard, /clients/new, /artists/new)
const ownerToken = makeFakeJwt({ role: "owner" });

// client role (redirects to /book for anything above ClientAndBelow)
const clientToken = makeFakeJwt({ role: "client" });
```

Inject in Playwright before navigating to the protected route:

```js
await page.goto("http://localhost:5173/login");   // set the origin first
await page.evaluate((t) => localStorage.setItem("auth_token", t), artistToken);
await page.goto("http://localhost:5173/designs", { waitUntil: "networkidle" });
```

**If the real backend is running, mock `/api/*` calls too.** The fake JWT has
no valid signature, so a real backend correctly 401s it — and the app's
global `baseQuery` 401-handler treats that as session-expired, logging out
and redirecting to `/login`. This silently masks whatever route/role behavior
you're testing (every role appears to land on the login page). Either stop
the backend so failures are network errors instead of 401s, or install
`page.route` mocks for `/api/*` (see Route mocking pitfalls below) so the
fake session never reaches real auth.

## Role → redirect map

| Role     | Default redirect (`getRoleRedirectPath`) |
|----------|------------------------------------------|
| `client` | `/book`                                  |
| `artist` | `/schedule`                              |
| `owner`  | `/dashboard`                             |
| `admin`  | `/platform`                              |

## Route mocking pitfalls

**Don't use `**/api/**` as a catch-all.** That glob also matches Vite's own
source-file requests like `http://localhost:5173/src/shared/api/filesApi.ts`,
which aborts the JS module graph and produces a blank page. Use an `isApiCall`
predicate instead:

```js
const isApiCall = (url) =>
  url.hostname === "localhost" && url.pathname.startsWith("/api/");
await page.route(isApiCall, (r) => r.abort());
```

**RTK Query appends a bare `?` on empty params.** `fetchBaseQuery` with
`params: {}` emits `/api/v1/designs?`, not `/api/v1/designs`. An exact string
pattern won't match. Use a URL-predicate function or a `*` suffix:

```js
// Predicate (recommended — no ambiguity):
await page.route((url) => url.pathname === "/api/v1/designs", handler);

// Glob suffix (also works):
await page.route(`${BASE}/api/v1/designs*`, handler);
```

**Route priority is last-in, first-matched.** Register your catch-all route
*first*, then add specific mocks *after* — they will take priority.

## Boilerplate script

Write a `.mjs` file and run with `node`:

```js
import { chromium } from "playwright";
import path from "path";
import { fileURLToPath } from "url";
import { makeFakeJwt } from "./.claude/skills/verifier-gui/fake-jwt.mjs";

const SS = path.dirname(fileURLToPath(import.meta.url));   // save screenshots here
const BASE = "http://localhost:5173";

(async () => {
  const browser = await chromium.launch({ headless: true });
  const ctx     = await browser.newContext();
  const page    = await ctx.newPage();

  // Inject auth
  await page.goto(`${BASE}/login`);
  await page.evaluate((t) => localStorage.setItem("auth_token", t),
    makeFakeJwt({ role: "artist" }));

  // Navigate to the page under test
  await page.goto(`${BASE}/designs`, { waitUntil: "networkidle" });

  // Assert + capture
  const header = await page.locator("header span.font-semibold").textContent();
  console.log("header:", header);
  await page.screenshot({ path: path.join(SS, "out.png"), fullPage: true });

  await browser.close();
})();
```

Run from the **repo root** so the relative `fake-jwt.mjs` import resolves:

```powershell
node C:\path\to\verify_script.mjs
```

## What to check per change type

| Change | What to drive | Probe |
|---|---|---|
| New page / route | Navigate to route as required role | Navigate as lower role → confirm redirect |
| New component on existing page | Navigate to page, check element present | Pass missing/null props equivalent (empty API response) |
| New filter / query param | Navigate with param in URL | Navigate without param — confirm default renders |
| Role guard change | Navigate as each boundary role | Check one role above and one below the new boundary |
| API query wired up | Confirm page renders without crash when API 404s | (backend not required) |

## Error-state behaviour (backend down)

When the backend is not running, Vite proxies return network errors.
RTK Query sets `isError = true`. Each page's error branch shows:
> "Failed to load [noun]. Please try again."

This is correct and expected — it is **not** a failure.

## Screenshots

Save to `C:\Users\User\AppData\Local\Temp\` or a `mktemp`-style dir.
Read them back with the `Read` tool to embed visual evidence in the report.
