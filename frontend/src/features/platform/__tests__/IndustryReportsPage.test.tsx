import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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
  http.post("http://localhost/api/v1/platform/reports/industry/trigger", () =>
    new HttpResponse(null, { status: 202 }),
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

  it("shows skeleton cards while loading, not a spinner", () => {
    renderPage();
    expect(document.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
    expect(screen.queryByText("Loading…")).not.toBeInTheDocument();
  });

  it("renders the Industry Reports header", async () => {
    renderPage();
    expect(await screen.findByText("Industry Reports")).toBeInTheDocument();
  });

  it("renders period names as formatted month labels", async () => {
    renderPage();
    expect(await screen.findByText("May 2026")).toBeInTheDocument();
    expect(screen.getByText("April 2026")).toBeInTheDocument();
  });

  it("shows generated date in 'D Mon YYYY' format for each report", async () => {
    renderPage();
    await screen.findByText("May 2026");
    // formatDate uses { day: "numeric", month: "short", year: "numeric" }
    // → "1 Jun 2026"
    expect(screen.getByText(/generated 1 jun 2026/i)).toBeInTheDocument();
  });

  it("renders Open links with the correct download URLs", async () => {
    renderPage();
    await screen.findByText("May 2026");

    const openLinks = screen.getAllByRole("link", { name: /open .+ industry report in new tab/i });
    expect(openLinks).toHaveLength(2);
    expect(openLinks[0]).toHaveAttribute("href", "https://cdn.example.com/reports/2026-05.pdf");
    expect(openLinks[1]).toHaveAttribute("href", "https://cdn.example.com/reports/2026-04.pdf");
  });

  it("opens links in a new tab", async () => {
    renderPage();
    await screen.findByText("May 2026");

    const [firstLink] = screen.getAllByRole("link", { name: /open .+ industry report in new tab/i });
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

    // New bold heading
    expect(await screen.findByText("No reports yet")).toBeInTheDocument();
    // Explanation paragraph references monthly schedule
    expect(screen.getByText(/generated automatically on the 1st of each month/i)).toBeInTheDocument();
    // Old copy must be gone
    expect(screen.queryByText(/no reports published yet/i)).not.toBeInTheDocument();
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

  it("shows helper text below the header", async () => {
    renderPage();
    await screen.findByText("May 2026");
    expect(screen.getByText(/anonymized platform-wide analytics/i)).toBeInTheDocument();
  });

  it("shows Generate Report button in header", async () => {
    renderPage();
    expect(
      screen.getByRole("button", { name: /trigger industry report generation now/i })
    ).toBeInTheDocument();
  });

  it("shows report count badge in header when reports exist", async () => {
    renderPage();
    await screen.findByText("May 2026");
    // 2 reports → badge shows "2"
    expect(screen.getByText("2", { selector: "span" })).toBeInTheDocument();
  });

  it("shows Download button for each report", async () => {
    renderPage();
    await screen.findByText("May 2026");
    const downloadLinks = screen.getAllByRole("link", { name: /download .+ industry report/i });
    expect(downloadLinks).toHaveLength(2);
  });

  it("Download links have the correct download attribute", async () => {
    renderPage();
    await screen.findByText("May 2026");

    const [may, april] = screen.getAllByRole("link", { name: /download .+ industry report/i });
    expect(may).toHaveAttribute("download", "industry-report-2026-05.json");
    expect(april).toHaveAttribute("download", "industry-report-2026-04.json");
  });

  it("clicking Generate Report posts to trigger endpoint and shows 'Queued' confirmation", async () => {
    const triggerSpy = vi.fn();
    server.use(
      http.post("http://localhost/api/v1/platform/reports/industry/trigger", () => {
        triggerSpy();
        return new HttpResponse(null, { status: 202 });
      }),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByText("May 2026");

    await user.click(
      screen.getByRole("button", { name: /trigger industry report generation now/i })
    );

    await waitFor(() => expect(triggerSpy).toHaveBeenCalledOnce());
    expect(await screen.findByText(/queued — report will appear shortly/i)).toBeInTheDocument();
    // The trigger button is gone while queued
    expect(
      screen.queryByRole("button", { name: /trigger industry report generation now/i })
    ).not.toBeInTheDocument();
  });

  it("empty state mentions the next report date", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/reports/industry", () =>
        HttpResponse.json([]),
      ),
    );
    renderPage();
    await screen.findByText("No reports yet");
    // The page renders the next 1st-of-month dynamically.
    // Assert the structure is present; exact date depends on test runtime.
    expect(screen.getByText(/the first report will appear here on/i)).toBeInTheDocument();
  });

  it("shows 'JSON' label on each report row", async () => {
    renderPage();
    await screen.findByText("May 2026");
    const jsonLabels = screen.getAllByText("JSON");
    expect(jsonLabels).toHaveLength(2);
  });

  it("shows error message when trigger endpoint returns 500", async () => {
    server.use(
      http.post("http://localhost/api/v1/platform/reports/industry/trigger", () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 }),
      ),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByText("May 2026");

    await user.click(
      screen.getByRole("button", { name: /trigger industry report generation now/i })
    );

    expect(await screen.findByText(/failed to queue — try again/i)).toBeInTheDocument();
    expect(screen.queryByText(/queued — report will appear shortly/i)).not.toBeInTheDocument();
  });
});
