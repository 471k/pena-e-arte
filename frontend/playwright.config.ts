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
