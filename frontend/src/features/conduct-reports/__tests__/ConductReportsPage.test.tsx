import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { conductReportsApi } from "@/features/conduct-reports/conductReportsApi";
import { ConductReportsPage } from "@/features/conduct-reports/components/ConductReportsPage";
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
  reporterUserId: "u-reporter-2", reporterName: "Jane Doe 2",
};

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me/conduct-reports", () =>
    HttpResponse.json([STANDARD_REPORT, HIGH_SEVERITY_REPORT])),
  http.get("http://localhost/api/v1/artists/me/conduct-reports", () =>
    // Backend redaction always nulls these for the artist read — simulate a hypothetical
    // payload leak here on purpose, to prove the frontend ALSO never renders it even if the
    // API response were somehow wrong (belt-and-suspenders on top of the backend test).
    HttpResponse.json([{ ...STANDARD_REPORT, reporterUserId: "leaked-id", reporterName: "Leaked Name" }])),
  http.patch("http://localhost/api/v1/conduct-reports/:id/status", () => new HttpResponse(null, { status: 204 })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

function makeStore(role: "owner" | "artist") {
  return configureStore({
    reducer: {
      auth: authReducer,
      [conductReportsApi.reducerPath]: conductReportsApi.reducer,
    },
    middleware: (gd) => gd().concat(conductReportsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: `${role}@test.com` }, token: "fake", tenantId: "t1", role } as any,
    },
  });
}

function renderPage(role: "owner" | "artist") {
  render(
    <Provider store={makeStore(role)}>
      <ConductReportsPage />
    </Provider>,
  );
}

describe("ConductReportsPage — owner view", () => {
  it("shows working status buttons for a Standard-severity report", async () => {
    const user = userEvent.setup();
    renderPage("owner");

    const toggle = await screen.findByRole("button", { name: /poor service quality/i, expanded: false });
    await user.click(toggle);
    const card = within(toggle.parentElement!);

    expect(card.getByRole("button", { name: /^resolved$/i })).toBeInTheDocument();
    expect(card.queryByText(/only pena e art. staff can close/i)).not.toBeInTheDocument();
  });

  it("shows locked/escalated copy instead of status buttons for a High-severity report", async () => {
    const user = userEvent.setup();
    renderPage("owner");

    const toggle = await screen.findByRole("button", { name: /sexual misconduct/i, expanded: false });
    await user.click(toggle);
    const card = within(toggle.parentElement!);

    expect(card.getByText(/only pena e art. staff can close this report/i)).toBeInTheDocument();
    expect(card.queryByRole("button", { name: /^resolved$/i })).not.toBeInTheDocument();
  });

  it("shows the reporter's real name for the owner", async () => {
    const user = userEvent.setup();
    renderPage("owner");

    const toggle = await screen.findByRole("button", { name: /poor service quality/i, expanded: false });
    await user.click(toggle);
    const card = within(toggle.parentElement!);

    // Scoped to this specific card — the second (High-severity) card's "Jane Doe 2" would
    // otherwise also match an unanchored /reported by jane doe/i on the page as a whole.
    expect(card.getByText(/reported by jane doe$/i)).toBeInTheDocument();
  });
});

describe("ConductReportsPage — artist view", () => {
  it("never renders reporter identity, even when the API payload includes it", async () => {
    renderPage("artist");

    await screen.findByText(/poor service quality/i);

    expect(screen.queryByText(/leaked name/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/leaked-id/i)).not.toBeInTheDocument();
    expect(screen.getByText(/reported by anonymous/i)).toBeInTheDocument();
  });

  it("shows no status-change controls for the artist", async () => {
    const user = userEvent.setup();
    renderPage("artist");

    const toggle = await screen.findByRole("button", { name: /poor service quality/i, expanded: false });
    await user.click(toggle);
    const card = within(toggle.parentElement!);

    expect(card.queryByRole("button", { name: /^resolved$/i })).not.toBeInTheDocument();
    expect(card.queryByRole("button", { name: /^dismissed$/i })).not.toBeInTheDocument();
  });
});
