import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { platformApi } from "@/features/platform/platformApi";
import { IndustryReportsPage } from "@/features/platform/components/IndustryReportsPage";
import type { IndustryReportSummary } from "@/features/platform/platform.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const REPORTS: IndustryReportSummary[] = [
  {
    period:      "2026-05",
    generatedAt: "2026-06-01T08:00:00Z",
    downloadUrl: "https://cdn.example.com/reports/2026-05.pdf",
  },
  {
    period:      "2026-04",
    generatedAt: "2026-05-02T09:30:00Z",
    downloadUrl: "https://cdn.example.com/reports/2026-04.pdf",
  },
];

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/platform/reports/industry", () =>
    HttpResponse.json(REPORTS),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                      authReducer,
      [platformApi.reducerPath]: platformApi.reducer,
    },
    middleware: (gd) => gd().concat(platformApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u4", email: "issuer@platform.test" }, token: "fake", tenantId: null, role: "issuer", pendingReferralCode: null } as any,
    },
  });
}

function renderPage() {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter>
        <IndustryReportsPage />
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("IndustryReportsPage", () => {

  it("shows a loading spinner while loading", () => {
    renderPage();
    expect(screen.getByText("Loading…")).toBeInTheDocument();
  });

  it("renders the Industry Reports header", async () => {
    renderPage();
    expect(await screen.findByText("Industry Reports")).toBeInTheDocument();
  });

  it("renders both report periods as formatted month names", async () => {
    renderPage();
    expect(await screen.findByText("May 2026")).toBeInTheDocument();
    expect(screen.getByText("April 2026")).toBeInTheDocument();
  });

  it("shows generated date for each report", async () => {
    renderPage();
    await screen.findByText("May 2026");
    // Generated 01/06/2026 (en-GB locale)
    expect(screen.getByText(/generated 01\/06\/2026/i)).toBeInTheDocument();
  });

  it("renders Open links with the correct download URLs", async () => {
    renderPage();
    await screen.findByText("May 2026");

    const openLinks = screen.getAllByRole("link", { name: /open/i });
    expect(openLinks).toHaveLength(2);
    expect(openLinks[0]).toHaveAttribute("href", "https://cdn.example.com/reports/2026-05.pdf");
    expect(openLinks[1]).toHaveAttribute("href", "https://cdn.example.com/reports/2026-04.pdf");
  });

  it("opens links in a new tab", async () => {
    renderPage();
    await screen.findByText("May 2026");

    const [firstLink] = screen.getAllByRole("link", { name: /open/i });
    expect(firstLink).toHaveAttribute("target", "_blank");
    expect(firstLink).toHaveAttribute("rel", "noopener noreferrer");
  });

  it("shows empty state when no reports exist", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/reports/industry", () =>
        HttpResponse.json([]),
      ),
    );
    renderPage();
    expect(await screen.findByText(/no reports published yet/i)).toBeInTheDocument();
  });

  it("shows error state when reports fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/reports/industry", () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/failed to load reports/i)).toBeInTheDocument();
  });
});
