import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { conductReportsApi } from "@/features/conduct-reports/conductReportsApi";
import { ConductReportInboxPage } from "@/features/conduct-reports/components/ConductReportInboxPage";
import type { ConductReportResponse } from "@/features/conduct-reports/conductReports.types";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));

const STANDARD_REPORT: ConductReportResponse = {
  id: "cr-1", studioId: "s1", studioName: "Ink Studio", artistId: "a1", artistName: "Maria Silva",
  appointmentId: "appt-1", appointmentDate: "2026-08-01T00:00:00.000Z",
  category: "PoorServiceQuality", isHighSeverity: false,
  reason: "The line work was sloppy.", attachmentUrls: [],
  status: "Open", resolutionNote: null, resolvedAt: null, createdAt: "2026-08-02T00:00:00.000Z",
  reporterUserId: "u-reporter", reporterName: "Jane Doe",
};

const HIGH_SEVERITY_REPORT: ConductReportResponse = {
  ...STANDARD_REPORT,
  id: "cr-2", category: "SexualMisconduct", isHighSeverity: true,
};

let lastUrl: URL | null = null;

const server = setupServer(
  http.get("http://localhost/api/v1/platform/conduct-reports", ({ request }) => {
    lastUrl = new URL(request.url);
    return HttpResponse.json([STANDARD_REPORT, HIGH_SEVERITY_REPORT]);
  }),
  http.patch("http://localhost/api/v1/conduct-reports/:id/status", () => new HttpResponse(null, { status: 204 })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      [conductReportsApi.reducerPath]: conductReportsApi.reducer,
    },
    middleware: (gd) => gd().concat(conductReportsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "admin@test.com" }, token: "fake", tenantId: null, role: "admin" } as any,
    },
  });
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <ConductReportInboxPage />
    </Provider>,
  );
}

describe("ConductReportInboxPage", () => {
  it("status controls are enabled regardless of severity", async () => {
    const user = userEvent.setup();
    renderPage();

    const standardToggle = await screen.findByRole("button", { name: /poor service quality/i, expanded: false });
    await user.click(standardToggle);
    expect(within(standardToggle.parentElement!).getByRole("button", { name: /^resolved$/i })).not.toBeDisabled();

    const highToggle = screen.getByRole("button", { name: /sexual misconduct/i, expanded: false });
    await user.click(highToggle);
    expect(within(highToggle.parentElement!).getByRole("button", { name: /^resolved$/i })).not.toBeDisabled();
  });

  it("clicking a status button calls updateConductReportStatus", async () => {
    const user = userEvent.setup();
    let captured: unknown = null;
    server.use(
      http.patch("http://localhost/api/v1/conduct-reports/:id/status", async ({ request }) => {
        captured = await request.json();
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderPage();

    const toggle = await screen.findByRole("button", { name: /sexual misconduct/i, expanded: false });
    await user.click(toggle);
    await user.click(within(toggle.parentElement!).getByRole("button", { name: /^resolved$/i }));

    await waitFor(() => expect(captured).toMatchObject({ status: "Resolved" }));
  });

  it("status filter narrows the list via the RTK Query params", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText(/poor service quality/i);

    await user.click(screen.getByRole("button", { name: /^resolved$/i }));

    await waitFor(() => expect(lastUrl?.searchParams.get("status")).toBe("Resolved"));
  });

  it("category filter narrows the list via the RTK Query params", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText(/poor service quality/i);

    await user.click(screen.getByRole("button", { name: /sexual misconduct or abuse/i }));

    await waitFor(() => expect(lastUrl?.searchParams.get("category")).toBe("SexualMisconduct"));
  });

  it("shows the reporter identity for the admin", async () => {
    renderPage();

    const matches = await screen.findAllByText(/reported by jane doe/i);
    expect(matches.length).toBeGreaterThan(0);
  });
});
