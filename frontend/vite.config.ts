import path from "path";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  server: {
    allowedHosts: ["test.tattooos.co"],
    proxy: {
      "/api": "http://localhost:5078",
      "/hubs": {
        target: "http://localhost:5078",
        ws: true,
      },
    },
  },
  test: {
    globals: false,
    environment: "jsdom",
    environmentOptions: {
      jsdom: { url: "http://localhost" },
    },
    setupFiles: ["./src/test/setup.ts"],
    // Vitest stubs every CSS import to an empty string by default — including `?raw`.
    // afterHoursTheme.test.ts needs index.css's real source text (it pins the theme tokens
    // and recomputes their contrast ratios), so that one file is let through. No component
    // under test imports index.css, so this changes nothing else. The same goes for the
    // two vendor stylesheets the contrast tests read as text: Tailwind's palette
    // (hardcodedTextColourContrast.test.ts) and Sonner's toast CSS (toastTheme.test.ts) —
    // nothing imports either as a stylesheet (Sonner injects its CSS from JavaScript).
    css: {
      include: [
        /src[\\/]index\.css(\?|$)/,
        /tailwindcss[\\/]theme\.css(\?|$)/,
        /sonner[\\/]dist[\\/]styles\.css(\?|$)/,
      ],
    },
    exclude: ["**/node_modules/**", "**/dist/**", "e2e/**"],
    // Default per-test timeout (5000ms) can be exceeded under full-suite parallel
    // worker load even when nothing is broken — see src/test/setup.ts's
    // asyncUtilTimeout comment for the same root cause. A test doing several
    // sequential findBy*/waitFor calls, each individually within its own budget,
    // can still add up past the outer per-test timeout under CPU contention.
    testTimeout: 10000,
    // GitHub Actions runners are weaker (2-core) than typical local dev hardware,
    // so the contention above is more consistently hit in CI — mirrors
    // playwright.config.ts's existing `retries: process.env.CI ? 2 : 0` for the
    // identical reason.
    retry: process.env.CI ? 2 : 0,
    // Locally, one worker per physical core instead of vitest's default (logical cores - 1).
    // Measured 2026-11-06 on a 2-core / 4-thread laptop with --coverage, full suite: 3 workers
    // took 19.7-20.7 min, with 16-19 tests over 10 s and 3 timeouts in one of three runs;
    // 2 workers took 22.3 min, with 7 over 10 s (all in files that carry their own overrides)
    // and every test ~30% faster (sum of test time 1818-1993 s -> 1369 s). The third worker bought
    // ~10% of wall-clock time and slowed every test it shared the CPU with. "50%" of logical
    // cores is roughly the physical cores on hyper-threaded machines. CI keeps the default: it
    // runs the suite ~3x faster than that laptop and was not measured for this.
    maxWorkers: process.env.CI ? undefined : "50%",
  },
});
