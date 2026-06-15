import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { designsApi } from "@/features/designs/designsApi";
import { DesignListPage } from "@/features/designs/components/DesignListPage";
import type { DesignResponse } from "@/features/designs/design.types";
import { Role } from "@/shared/types/roles";

// ── Seed data ──────────────────────────────────────────────────────────────────

const DESIGN_A: DesignResponse = {
  id:          "d-001",
  studioId:    "s-001",
  clientId:    "c-001",
  artistId:    "a-001",
  title:       "Dragon Sleeve",
  description: "Full sleeve concept",
  createdAt:   "2024-01-15T10:00:00Z",
};

const DESIGN_B: DesignResponse = {
  id:          "d-002",
  studioId:    "s-001",
  clientId:    "c-002",
  artistId:    "a-001",
  title:       "Rose Chest",
  description: null,
  createdAt:   "2024-02-01T10:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/designs", () =>
    HttpResponse.json([DESIGN_A, DESIGN_B]),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function makeStore(role: Role = Role.Owner) {
  return configureStore({
    reducer: {
      auth:                          authReducer,
      ui:                            uiReducer,
      [designsApi.reducerPath]:      designsApi.reducer,
    },
    middleware: (gd) => gd().concat(designsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "test@test.com" }, token: "fake-token", tenantId: "s-001", role, pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false },
    },
  });
}

function renderPage(role: Role = Role.Owner) {
  render(
    <Provider store={makeStore(role)}>
      <MemoryRouter initialEntries={["/designs"]}>
        <Routes>
          <Route path="/designs"          element={<DesignListPage />} />
          <Route path="/designs/new"      element={<div data-testid="create-page" />} />
          <Route path="/designs/:id"      element={<div data-testid="detail-page" />} />
          <Route path="/designs/:id/upload" element={<div data-testid="upload-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("DesignListPage", () => {

  it("renders the Designs header", () => {
    renderPage();
    expect(screen.getByText("Designs")).toBeInTheDocument();
  });

  it("shows an error message when the designs fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/designs", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText("Failed to load designs. Please try again.")).toBeInTheDocument();
  });

  it("shows empty-state text when no designs exist", async () => {
    server.use(
      http.get("http://localhost/api/v1/designs", () => HttpResponse.json([])),
    );
    renderPage();
    expect(await screen.findByText("No designs in this studio yet.")).toBeInTheDocument();
  });

  it("renders a card for each design returned by the API", async () => {
    renderPage();
    expect(await screen.findByText("Dragon Sleeve")).toBeInTheDocument();
    expect(screen.getByText("Rose Chest")).toBeInTheDocument();
  });

  it("shows the description when a design has one", async () => {
    renderPage();
    expect(await screen.findByText("Full sleeve concept")).toBeInTheDocument();
  });

  it("shows '2 designs' count in the header", async () => {
    renderPage();
    expect(await screen.findByText("2 designs")).toBeInTheDocument();
  });

  it("uses singular 'design' in the count when only one design exists", async () => {
    server.use(
      http.get("http://localhost/api/v1/designs", () => HttpResponse.json([DESIGN_A])),
    );
    renderPage();
    expect(await screen.findByText("1 design")).toBeInTheDocument();
  });

  it("'New Design' button is visible for the Artist role", async () => {
    renderPage(Role.Artist);
    await screen.findByText("Dragon Sleeve");
    expect(screen.getByRole("button", { name: /new design/i })).toBeInTheDocument();
  });

  it("'New Design' button is NOT visible for the Client role", async () => {
    renderPage(Role.Client);
    await screen.findByText("Dragon Sleeve");
    expect(screen.queryByRole("button", { name: /new design/i })).not.toBeInTheDocument();
  });

  it("'New Design' button navigates to /designs/new", async () => {
    const user = userEvent.setup();
    renderPage(Role.Artist);
    await screen.findByText("Dragon Sleeve");
    await user.click(screen.getByRole("button", { name: /new design/i }));
    expect(screen.getByTestId("create-page")).toBeInTheDocument();
  });

  it("clicking the design title link navigates to the detail page", async () => {
    const user = userEvent.setup();
    renderPage(Role.Artist);
    const link = await screen.findByRole("link", { name: /dragon sleeve/i });
    await user.click(link);
    expect(screen.getByTestId("detail-page")).toBeInTheDocument();
  });

  it("upload button is visible for Artist role within each card", async () => {
    renderPage(Role.Artist);
    await screen.findByText("Dragon Sleeve");
    expect(screen.getAllByRole("button", { name: /upload revision/i })).toHaveLength(2);
  });

  it("upload button is NOT visible for Client role within design cards", async () => {
    renderPage(Role.Client);
    await screen.findByText("Dragon Sleeve");
    expect(screen.queryByRole("button", { name: /upload revision/i })).not.toBeInTheDocument();
  });
});
