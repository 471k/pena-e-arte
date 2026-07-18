import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { artistsApi } from "@/features/artists/artistsApi";
import type { ArtistResponse } from "@/features/artists/artistsApi";
import { billingApi } from "@/features/billing/billingApi";
import type { PlanUsageResponse } from "@/features/billing/billing.types";
import { ArtistListPage } from "@/features/artists/components/ArtistListPage";

// ── Seed data ──────────────────────────────────────────────────────────────────

const ARTIST_A: ArtistResponse = {
  id:              "artist-0001",
  studioId:        "stud-0001",
  firstName:       "Ana",
  lastName:        "Costa",
  email:           "ana@ink.test",
  specializations: "Realism, Blackwork",
  hourlyRate:      null,
  isActive:        true,
  avatarUrl:       null,
  portfolioImages: [],
  slug: null,
  userId:          null,
  createdAt:       "2024-01-01T00:00:00Z",
  updatedAt:       "2024-01-01T00:00:00Z",
};

const ARTIST_B: ArtistResponse = {
  id:              "artist-0002",
  studioId:        "stud-0001",
  firstName:       "Marco",
  lastName:        "Silva",
  email:           "marco@ink.test",
  specializations: null,
  hourlyRate:      null,
  isActive:        true,
  avatarUrl:       null,
  portfolioImages: [],
  slug: null,
  userId:          null,
  createdAt:       "2024-01-02T00:00:00Z",
  updatedAt:       "2024-01-02T00:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/artists", () =>
    HttpResponse.json([ARTIST_A, ARTIST_B]),
  ),
  http.delete("http://localhost/api/v1/artists/:id", () =>
    new HttpResponse(null, { status: 204 }),
  ),
  http.get("http://localhost/api/v1/billing/usage", () =>
    HttpResponse.json(null),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Store / render helpers ─────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      ui:   uiReducer,
      [artistsApi.reducerPath]: artistsApi.reducer,
      [billingApi.reducerPath]: billingApi.reducer,
    },
    middleware: (gd) => gd().concat(artistsApi.middleware, billingApi.middleware),
    preloadedState: {
      auth: {
        user: { id: "u1", email: "owner@ink.test" },
        token: "fake-token",
        tenantId: "stud-0001",
        role: "owner",
        pendingReferralCode: null,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
      ui: { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null },
    },
  });
}

function makeStoreAsArtist() {
  return configureStore({
    reducer: {
      auth: authReducer,
      ui:   uiReducer,
      [artistsApi.reducerPath]: artistsApi.reducer,
      [billingApi.reducerPath]: billingApi.reducer,
    },
    middleware: (gd) => gd().concat(artistsApi.middleware, billingApi.middleware),
    preloadedState: {
      auth: {
        user: { id: "u2", email: "artist@ink.test" },
        token: "fake-token",
        tenantId: "stud-0001",
        role: "artist",
        pendingReferralCode: null,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
      ui: { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null },
    },
  });
}

function renderPage() {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/artists"]}>
        <Routes>
          <Route path="/artists"     element={<ArtistListPage />} />
          <Route path="/artists/:id" element={<div data-testid="artist-detail" />} />
          <Route path="/artists/new" element={<div data-testid="artist-new" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

function renderPageAsArtist() {
  const store = makeStoreAsArtist();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/artists"]}>
        <Routes>
          <Route path="/artists"     element={<ArtistListPage />} />
          <Route path="/artists/:id" element={<div data-testid="artist-detail" />} />
          <Route path="/artists/new" element={<div data-testid="artist-new" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("ArtistListPage", () => {
  it("renders the Artists page heading", async () => {
    renderPage();
    expect(await screen.findByText("Artists", { selector: "span.font-semibold" })).toBeInTheDocument();
  });

  it("does not show artist names while loading", () => {
    renderPage();
    expect(screen.queryByText("Ana Costa")).not.toBeInTheDocument();
  });

  it("renders artist full names", async () => {
    renderPage();
    expect(await screen.findByText("Ana Costa")).toBeInTheDocument();
    expect(screen.getByText("Marco Silva")).toBeInTheDocument();
  });

  it("renders initials avatar for each artist", async () => {
    renderPage();
    await screen.findByText("Ana Costa");
    expect(screen.getByText("AC")).toBeInTheDocument();
    expect(screen.getByText("MS")).toBeInTheDocument();
  });

  it("renders specialization chips when present", async () => {
    renderPage();
    await screen.findByText("Ana Costa");
    expect(screen.getAllByText("Realism").length).toBeGreaterThanOrEqual(1);
    expect(screen.getAllByText("Blackwork").length).toBeGreaterThanOrEqual(1);
  });

  it("renders em-dash placeholder when specializations are null", async () => {
    renderPage();
    await screen.findByText("Marco Silva");
    expect(screen.getAllByText("—").length).toBeGreaterThanOrEqual(1);
  });

  it("clicking an artist row navigates to /artists/:id", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Costa");

    await user.click(screen.getByText("Ana Costa"));

    expect(screen.getByTestId("artist-detail")).toBeInTheDocument();
  });

  it("search input is present on the page", async () => {
    renderPage();
    await screen.findByText("Ana Costa");
    expect(screen.getByPlaceholderText(/search/i)).toBeInTheDocument();
  });

  it("shows an error message when artists fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/artists", () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/failed to load/i)).toBeInTheDocument();
  });

  it("shows empty state when no artists are returned", async () => {
    server.use(
      http.get("http://localhost/api/v1/artists", () =>
        HttpResponse.json([]),
      ),
    );
    renderPage();
    await screen.findByText(/no artists/i);
  });

  // ── Actions column ──────────────────────────────────────────────────────────

  it("Edit button navigates to /artists/:id", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Costa");

    const editButtons = screen.getAllByRole("button", { name: /^edit$/i });
    await user.click(editButtons[0]);

    expect(screen.getByTestId("artist-detail")).toBeInTheDocument();
  });

  it("Delete button is visible to owners", async () => {
    renderPage();
    await screen.findByText("Ana Costa");
    expect(screen.getAllByRole("button", { name: /^delete$/i }).length).toBeGreaterThanOrEqual(1);
  });

  it("Delete button is NOT visible to non-owners", async () => {
    renderPageAsArtist();
    await screen.findByText("Ana Costa");
    expect(screen.queryByRole("button", { name: /^delete$/i })).not.toBeInTheDocument();
  });

  it("clicking Delete shows inline confirmation for that artist", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Costa");

    await user.click(screen.getAllByRole("button", { name: /^delete$/i })[0]);

    expect(screen.getByText(/delete ana costa\?/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^cancel$/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^confirm$/i })).toBeInTheDocument();
  });

  it("clicking Cancel hides the delete confirmation", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Costa");

    await user.click(screen.getAllByRole("button", { name: /^delete$/i })[0]);
    expect(screen.getByText(/delete ana costa\?/i)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /^cancel$/i }));

    expect(screen.queryByText(/delete ana costa\?/i)).not.toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: /^delete$/i }).length).toBeGreaterThanOrEqual(1);
  });

  it("confirming delete calls DELETE /artists/:id", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Costa");

    await user.click(screen.getAllByRole("button", { name: /^delete$/i })[0]);
    await user.click(screen.getByRole("button", { name: /^confirm$/i }));

    await waitFor(() => {
      expect(screen.queryByText(/delete ana costa\?/i)).not.toBeInTheDocument();
    });
  });

  // ── Specialization filter ───────────────────────────────────────────────────

  it("spec filter buttons appear for specs in the loaded data", async () => {
    renderPage();
    await screen.findByText("Ana Costa");

    expect(screen.getByRole("button", { name: "Realism" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Blackwork" })).toBeInTheDocument();
  });

  it("clicking a spec filter button filters the table to matching artists", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Costa");

    await user.click(screen.getByRole("button", { name: "Realism" }));

    expect(screen.getByText("Ana Costa")).toBeInTheDocument();
    expect(screen.queryByText("Marco Silva")).not.toBeInTheDocument();
  });

  it("clicking the active spec filter button again clears the filter", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Costa");

    await user.click(screen.getByRole("button", { name: "Realism" }));
    expect(screen.queryByText("Marco Silva")).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Realism" }));
    expect(screen.getByText("Marco Silva")).toBeInTheDocument();
  });

  // ── Rich empty state ────────────────────────────────────────────────────────

  it("shows rich empty state with icon text and CTA when zero artists", async () => {
    server.use(
      http.get("http://localhost/api/v1/artists", () => HttpResponse.json([])),
    );
    renderPage();

    expect(await screen.findByText("No artists yet")).toBeInTheDocument();
    expect(screen.getByText(/add your first artist/i)).toBeInTheDocument();
  });

  it("rich empty state New Artist button navigates to /artists/new", async () => {
    const user = userEvent.setup();
    server.use(
      http.get("http://localhost/api/v1/artists", () => HttpResponse.json([])),
    );
    renderPage();

    await screen.findByText("No artists yet");
    await user.click(screen.getByRole("button", { name: /new artist/i }));

    expect(screen.getByTestId("artist-new")).toBeInTheDocument();
  });

  // ── Plan usage indicator ─────────────────────────────────────────────────────

  it("does not show a usage indicator when the plan has unlimited artists (null max)", async () => {
    renderPage();
    await screen.findByText("Ana Costa");
    expect(screen.queryByText(/artists used/i)).not.toBeInTheDocument();
  });

  it("shows '2 of 6 artists used' when usage data has a cap", async () => {
    const usage: PlanUsageResponse = {
      planName: "Starter",
      artists: { current: 2, max: 6 },
      appointmentsPerMonth:  { current: 0, max: null },
      notificationsPerMonth: { current: 0, max: null },
      storageGb:              { current: 0, max: null },
      locations:               { current: 1, max: null },
    };
    server.use(
      http.get("http://localhost/api/v1/billing/usage", () => HttpResponse.json(usage)),
    );
    renderPage();
    await screen.findByText("Ana Costa");

    expect(await screen.findByText("2 of 6 artists used")).toBeInTheDocument();
  });

  it("does not fetch plan usage for non-owner roles (OwnerOnly endpoint)", async () => {
    const usageSpy = vi.fn();
    server.use(
      http.get("http://localhost/api/v1/billing/usage", () => {
        usageSpy();
        return HttpResponse.json(null);
      }),
    );
    renderPageAsArtist();
    await screen.findByText("Ana Costa");

    expect(usageSpy).not.toHaveBeenCalled();
  });
});
