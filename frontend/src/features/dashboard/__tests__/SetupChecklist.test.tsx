import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { artistsApi } from "@/features/artists/artistsApi";
import { depositRulesApi } from "@/features/deposit-rules/depositRulesApi";
import { SetupChecklist } from "@/features/dashboard/components/SetupChecklist";
import type { ArtistResponse } from "@/features/artists/artistsApi";

const ARTIST: ArtistResponse = {
  id: "artist-0001", studioId: "stud-0001", firstName: "Ana", lastName: "Costa",
  email: "ana@ink.test", specializations: null, hourlyRate: null, isActive: true,
  avatarUrl: null, portfolioImages: [], slug: null, userId: null,
  createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z",
};

let artistsResponse: ArtistResponse[] = [];
let depositRulesResponse: unknown[] = [];

const server = setupServer(
  http.get("http://localhost/api/v1/artists", () => HttpResponse.json(artistsResponse)),
  http.get("http://localhost/api/v1/deposit-rules", () => HttpResponse.json(depositRulesResponse)),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); artistsResponse = []; depositRulesResponse = []; });
afterAll(() => server.close());

function renderChecklist() {
  const store = configureStore({
    reducer: {
      auth: authReducer,
      [artistsApi.reducerPath]:     artistsApi.reducer,
      [depositRulesApi.reducerPath]: depositRulesApi.reducer,
    },
    middleware: (gd) => gd().concat(artistsApi.middleware, depositRulesApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "owner@ink.test" }, token: "fake-token", tenantId: "stud-0001", role: "owner", pendingReferralCode: null } as any,
    },
  });
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/dashboard"]}>
        <Routes>
          <Route path="/dashboard" element={<SetupChecklist />} />
          <Route path="/artists/new" element={<div data-testid="new-artist-page" />} />
          <Route path="/deposit-rules/new" element={<div data-testid="new-deposit-rule-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

describe("SetupChecklist", () => {
  it("shows both steps as incomplete when there are no artists or deposit rules", async () => {
    renderChecklist();
    expect(await screen.findByTestId("setup-checklist")).toBeInTheDocument();
    expect(screen.getByText("Add your first artist")).toBeInTheDocument();
    expect(screen.getByText("Set a deposit rule")).toBeInTheDocument();
    expect(screen.getByText("0/2 complete")).toBeInTheDocument();
  });

  it("marks 'Add your first artist' done once an artist exists", async () => {
    artistsResponse = [ARTIST];
    renderChecklist();
    expect(await screen.findByText("1/2 complete")).toBeInTheDocument();
  });

  it("renders null (no card) once every step is complete", async () => {
    artistsResponse = [ARTIST];
    depositRulesResponse = [{ id: "rule-1" }];
    renderChecklist();
    await waitFor(() => expect(screen.queryByTestId("setup-checklist")).not.toBeInTheDocument());
  });

  it("'Set rule' button navigates to /deposit-rules/new", async () => {
    const user = userEvent.setup();
    renderChecklist();
    await screen.findByText("Set a deposit rule");

    await user.click(screen.getByRole("button", { name: "Set rule" }));

    expect(screen.getByTestId("new-deposit-rule-page")).toBeInTheDocument();
  });
});
