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
  },
});
