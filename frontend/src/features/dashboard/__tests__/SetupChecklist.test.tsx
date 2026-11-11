import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import {
  studiosApi,
  type SetupAreaState,
  type SetupReadinessResponse,
} from "@/features/studios/studiosApi";
import { SetupChecklist } from "@/features/dashboard/components/SetupChecklist";

// E05a (O02): the checklist reads GET /studios/me/setup-readiness, which reports each area as
// NotConfigured / DefaultUnreviewed / Reviewed. It no longer counts artist, deposit-rule or hours rows.
function readiness(artists: SetupAreaState, depositRules: SetupAreaState, hours: SetupAreaState): SetupReadinessResponse {
  return {
    areas: [
      { area: "Artists",      state: artists,      reviewedAt: null },
      { area: "DepositRules", state: depositRules, reviewedAt: null },
      { area: "Hours",        state: hours,        reviewedAt: hours === "Reviewed" ? "2026-11-07T12:00:00Z" : null },
    ],
  };
}

let readinessResponse: SetupReadinessResponse = readiness("NotConfigured", "NotConfigured", "NotConfigured");
let readinessStatus = 200;
let confirmCalls = 0;

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me/setup-readiness", () =>
    readinessStatus === 200
      ? HttpResponse.json(readinessResponse)
      : HttpResponse.json({ title: "boom" }, { status: readinessStatus }),
  ),
  http.post("http://localhost/api/v1/studios/me/hours/confirm", () => {
    confirmCalls += 1;
    readinessResponse = {
      areas: readinessResponse.areas.map((a) => (a.area === "Hours" ? { ...a, state: "Reviewed" } : a)),
    };
    return new HttpResponse(null, { status: 204 });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  server.resetHandlers();
  cleanup();
  readinessResponse = readiness("NotConfigured", "NotConfigured", "NotConfigured");
  readinessStatus = 200;
  confirmCalls = 0;
});
afterAll(() => server.close());

function renderChecklist() {
  const store = configureStore({
    reducer: {
      auth: authReducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(studiosApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "owner@ink.test" }, token: "fake-token", tenantId: "stud-0001", role: "owner", pendingReferralCode: null, impersonation: null } as any,
    },
  });
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/dashboard"]}>
        <Routes>
          <Route path="/dashboard" element={<SetupChecklist />} />
          <Route path="/artists/new" element={<div data-testid="new-artist-page" />} />
          <Route path="/deposit-rules/new" element={<div data-testid="new-deposit-rule-page" />} />
          <Route path="/studios/me" element={<div data-testid="studio-settings-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

describe("SetupChecklist", () => {
  it("shows all three steps as incomplete when nothing is set up", async () => {
    renderChecklist();
    expect(await screen.findByTestId("setup-checklist")).toBeInTheDocument();
    expect(screen.getByText("Add your first artist")).toBeInTheDocument();
    expect(screen.getByText("Set a deposit rule")).toBeInTheDocument();
    expect(screen.getByText("Set your hours")).toBeInTheDocument();
    expect(screen.getByText("0/3 complete")).toBeInTheDocument();
  });

  it("marks 'Add your first artist' done once the server reports artists reviewed", async () => {
    readinessResponse = readiness("Reviewed", "NotConfigured", "NotConfigured");
    renderChecklist();
    expect(await screen.findByText("1/3 complete")).toBeInTheDocument();
  });

  // Replaces "marks 'Set your hours' done once hours rows exist", which asserted the O02 bug:
  // registration seeds hours rows, so rows existing proves nothing about the owner reviewing them.
  it("does NOT mark hours done for registration's seeded hours: it says they were set for the owner", async () => {
    readinessResponse = readiness("NotConfigured", "NotConfigured", "DefaultUnreviewed");
    renderChecklist();
    expect(await screen.findByText("0/3 complete")).toBeInTheDocument();
    const hours = screen.getByTestId("setup-area-Hours");
    expect(hours).toHaveAttribute("data-state", "DefaultUnreviewed");
    expect(within(hours).getByText("We've set opening hours for you. Check they're right.")).toBeInTheDocument();
    expect(within(hours).getByText(/set for you, not yet reviewed/)).toBeInTheDocument();
    expect(within(hours).getByText("Set your hours")).not.toHaveClass("line-through");
  });

  it("never claims setup is complete while the hours are only the seeded default", async () => {
    readinessResponse = readiness("Reviewed", "Reviewed", "DefaultUnreviewed");
    renderChecklist();
    expect(await screen.findByTestId("setup-checklist")).toBeInTheDocument();
    expect(screen.getByText("2/3 complete")).toBeInTheDocument();
    expect(screen.queryByText("3/3 complete")).not.toBeInTheDocument();
  });

  it("shows each of the three states distinctly", async () => {
    readinessResponse = readiness("Reviewed", "NotConfigured", "DefaultUnreviewed");
    renderChecklist();
    await screen.findByTestId("setup-checklist");
    expect(within(screen.getByTestId("setup-area-Artists")).getByText(/\(done\)/)).toBeInTheDocument();
    expect(within(screen.getByTestId("setup-area-DepositRules")).getByText(/not set up yet/)).toBeInTheDocument();
    expect(within(screen.getByTestId("setup-area-Hours")).getByText(/set for you, not yet reviewed/)).toBeInTheDocument();
    expect(within(screen.getByTestId("setup-area-Artists")).queryByRole("button")).not.toBeInTheDocument();
  });

  it("'They're right' confirms the seeded hours, and the card goes once everything is reviewed", async () => {
    const user = userEvent.setup();
    readinessResponse = readiness("Reviewed", "Reviewed", "DefaultUnreviewed");
    renderChecklist();
    await user.click(await screen.findByRole("button", { name: "They're right" }));
    expect(confirmCalls).toBe(1);
    await waitFor(() => expect(screen.queryByTestId("setup-checklist")).not.toBeInTheDocument());
  });

  it("'Check hours' on seeded hours goes to Studio Settings", async () => {
    const user = userEvent.setup();
    readinessResponse = readiness("Reviewed", "Reviewed", "DefaultUnreviewed");
    renderChecklist();
    await user.click(await screen.findByRole("button", { name: "Check hours" }));
    expect(screen.getByTestId("studio-settings-page")).toBeInTheDocument();
  });

  it("offers no 'They're right' for hours that don't exist: a missing area can't be confirmed away", async () => {
    readinessResponse = readiness("Reviewed", "Reviewed", "NotConfigured");
    renderChecklist();
    expect(await screen.findByRole("button", { name: "Set hours" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "They're right" })).not.toBeInTheDocument();
  });

  it("renders null (no card) once every step is complete", async () => {
    readinessResponse = readiness("Reviewed", "Reviewed", "Reviewed");
    renderChecklist();
    await waitFor(() => expect(screen.queryByTestId("setup-checklist")).not.toBeInTheDocument());
  });

  it("says it couldn't check, rather than hiding as if complete, when the read fails", async () => {
    readinessStatus = 500;
    renderChecklist();
    expect(await screen.findByRole("alert")).toHaveTextContent("We couldn't check your studio setup just now.");
    expect(screen.getByRole("button", { name: "Try again" })).toBeInTheDocument();
  });

  it("treats a response without an areas list as a failed read, never as set up", async () => {
    server.use(http.get("http://localhost/api/v1/studios/me/setup-readiness", () => HttpResponse.json([])));
    renderChecklist();
    expect(await screen.findByRole("alert")).toHaveTextContent("We couldn't check your studio setup just now.");
  });

  it("'Set rule' button navigates to /deposit-rules/new", async () => {
    const user = userEvent.setup();
    renderChecklist();
    await screen.findByText("Set a deposit rule");

    await user.click(screen.getByRole("button", { name: "Set rule" }));

    expect(screen.getByTestId("new-deposit-rule-page")).toBeInTheDocument();
  });

  it("'Set hours' button navigates to /studios/me", async () => {
    const user = userEvent.setup();
    renderChecklist();
    await screen.findByText("Set your hours");

    await user.click(screen.getByRole("button", { name: "Set hours" }));

    expect(screen.getByTestId("studio-settings-page")).toBeInTheDocument();
  });

  it("'Add artist' button navigates to /artists/new", async () => {
    const user = userEvent.setup();
    renderChecklist();
    await user.click(await screen.findByRole("button", { name: "Add artist" }));
    expect(screen.getByTestId("new-artist-page")).toBeInTheDocument();
  });
  // ── A07a: the pay model, a fourth area ──────────────────────────────────

  function withPayModel(state: SetupAreaState): SetupReadinessResponse {
    return {
      areas: [
        ...readiness("Reviewed", "Reviewed", "Reviewed").areas,
        { area: "PayModel", state, reviewedAt: state === "Reviewed" ? "2026-11-11T10:00:00Z" : null },
      ],
    };
  }

  it("shows the pay model as set for the owner but not reviewed, with no 'They're right'", async () => {
    readinessResponse = withPayModel("DefaultUnreviewed");
    renderChecklist();
    expect(await screen.findByText("3/4 complete")).toBeInTheDocument();
    const payModel = screen.getByTestId("setup-area-PayModel");
    expect(within(payModel).getByText("Set how artists get paid")).toBeInTheDocument();
    expect(within(payModel).getByText(/no rate set, so artists can't see their share yet/)).toBeInTheDocument();
    expect(within(payModel).getByText(/set for you, not yet reviewed/)).toBeInTheDocument();
    // Registration's commission-with-no-rate default can't be confirmed (the server refuses it).
    expect(within(payModel).queryByRole("button", { name: "They're right" })).not.toBeInTheDocument();
    // And the hours copy doesn't leak onto it.
    expect(within(payModel).queryByText(/opening hours/)).not.toBeInTheDocument();
  });

  it("'Set pay model' goes to Studio Settings", async () => {
    const user = userEvent.setup();
    readinessResponse = withPayModel("DefaultUnreviewed");
    renderChecklist();
    await user.click(await screen.findByRole("button", { name: "Set pay model" }));
    expect(screen.getByTestId("studio-settings-page")).toBeInTheDocument();
  });

  it("goes once the pay model is reviewed too", async () => {
    readinessResponse = withPayModel("Reviewed");
    renderChecklist();
    await waitFor(() => expect(screen.queryByTestId("setup-checklist")).not.toBeInTheDocument());
  });
});
