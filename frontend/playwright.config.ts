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
  // Two full projects, not one — this app has no manual light/dark toggle, it renders
  // purely from `prefers-color-scheme` (see index.css), and Playwright's own default
  // color scheme is "light". A single "chromium" project here previously meant every
  // e2e run — locally and in CI — silently only ever exercised the light-mode styles;
  // the entire dark theme (most of this app's real-world traffic, since Chromium/most
  // OSes commonly default to dark) had zero coverage. That gap is exactly how a real
  // bug (an earlier version of index.css's dark-mode `@theme` block using a construct
  // Tailwind v4 silently collapses to unconditional, i.e. "dark for every visitor
  // regardless of preference") went undetected until axe-core's contrast checks were
  // added and happened to be run once under a real dark render. Two projects means
  // both states are covered on every run from now on, in CI included.
  projects: [
    {
      name: "chromium-light",
      use: { ...devices["Desktop Chrome"], colorScheme: "light" },
    },
    {
      name: "chromium-dark",
      use: { ...devices["Desktop Chrome"], colorScheme: "dark" },
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
