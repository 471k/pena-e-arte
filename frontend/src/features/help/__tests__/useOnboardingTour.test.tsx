import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { authApi } from "@/features/auth/authApi";
import { onboardingApi } from "../onboardingApi";
import type { OwnerTourFacts } from "../tours/ownerTour";
import { useOnboardingTour } from "../useOnboardingTour";
import { Role } from "@/shared/types/roles";

let tourStatusResponse: { hasCompletedTour: boolean } = { hasCompletedTour: false };
let completeCallCount = 0;

const server = setupServer(
  http.get("http://localhost/api/v1/onboarding/tour-status", () => HttpResponse.json(tourStatusResponse)),
  http.post("http://localhost/api/v1/onboarding/tour-complete", () => {
    completeCallCount++;
    return new HttpResponse(null, { status: 204 });
  }),
  http.get("http://localhost/api/v1/auth/my-studios", () => HttpResponse.json([])),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); tourStatusResponse = { hasCompletedTour: false }; completeCallCount = 0; });
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      [authApi.reducerPath]: authApi.reducer,
      [onboardingApi.reducerPath]: onboardingApi.reducer,
    },
    middleware: (gd) => gd().concat(authApi.middleware, onboardingApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "owner@test.com" }, token: "fake", tenantId: "t1", role: "owner" } as any,
    },
  });
}

const facts = (hasArtistProfile: boolean): OwnerTourFacts =>
  ({ hasArtistProfile, showsPublishBanner: false, hasJoinInvite: false });

// `ownerTourFacts` undefined = still loading (no default: a default would replace an explicit undefined).
function TestHost({ role, ownerTourFacts }: { role: Role | null; ownerTourFacts: OwnerTourFacts | undefined }) {
  const { tourElement, restartTour } = useOnboardingTour(role, undefined, undefined, false, ownerTourFacts);
  return (
    <>
      <button onClick={restartTour}>Restart</button>
      {tourElement}
      {/* A real nav target so the tour's first step can resolve. */}
      <button data-tour="owner-dashboard-nav">Dashboard</button>
    </>
  );
}

function renderHost(role: Role | null = Role.Owner) {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <TestHost role={role} ownerTourFacts={facts(true)} />
      </MemoryRouter>
    </Provider>,
  );
}

function renderOwnerHost(ownerTourFacts: OwnerTourFacts | undefined) {
  const store = makeStore();
  const ui = (value: OwnerTourFacts | undefined) => (
    <Provider store={store}>
      <MemoryRouter>
        <TestHost role={Role.Owner} ownerTourFacts={value} />
      </MemoryRouter>
    </Provider>
  );
  const { rerender } = render(ui(ownerTourFacts));
  return { setOwnerTourFacts: (value: OwnerTourFacts | undefined) => rerender(ui(value)) };
}

describe("useOnboardingTour", () => {
  it("shows the tour when the status query resolves to not-completed", async () => {
    tourStatusResponse = { hasCompletedTour: false };
    renderHost();

    expect(await screen.findByRole("dialog", {}, { timeout: 3000 })).toBeInTheDocument();
  });

  it("does not show the tour when already completed", async () => {
    tourStatusResponse = { hasCompletedTour: true };
    renderHost();

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    // give the status query a moment to have resolved either way
    await new Promise((r) => setTimeout(r, 300));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("marks the tour complete (does not re-launch) once skipped", async () => {
    const user = userEvent.setup();
    tourStatusResponse = { hasCompletedTour: false };
    renderHost();

    await screen.findByRole("dialog", {}, { timeout: 3000 });
    await user.keyboard("{Escape}");

    await waitFor(() => expect(completeCallCount).toBe(1));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("an owner's tour waits until its conditional steps are known, instead of timing steps out", async () => {
    const { setOwnerTourFacts } = renderOwnerHost(undefined);

    // The tour status is long back by now; the tour still waits.
    await new Promise((r) => setTimeout(r, 300));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    setOwnerTourFacts(facts(true));
    expect(await screen.findByRole("dialog", {}, { timeout: 3000 })).toBeInTheDocument();
  });

  it("an owner without an artist profile still gets the tour", async () => {
    renderOwnerHost(facts(false));

    expect(await screen.findByRole("dialog", {}, { timeout: 3000 })).toBeInTheDocument();
  });

  it("'Take the tour again' (restartTour) bypasses the completed-check", async () => {
    const user = userEvent.setup();
    tourStatusResponse = { hasCompletedTour: true };
    renderHost();

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /restart/i }));

    expect(await screen.findByRole("dialog", {}, { timeout: 3000 })).toBeInTheDocument();
  });
});
