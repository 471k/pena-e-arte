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
import { artistsApi } from "@/features/artists/artistsApi";
import type { ArtistResponse } from "@/features/artists/artistsApi";
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
  createdAt:       "2024-01-02T00:00:00Z",
  updatedAt:       "2024-01-02T00:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/artists", () =>
    HttpResponse.json([ARTIST_A, ARTIST_B]),
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
    },
    middleware: (gd) => gd().concat(artistsApi.middleware),
    preloadedState: {
      auth: {
        user: { id: "u1", email: "owner@ink.test" },
        token: "fake-token",
        tenantId: "stud-0001",
        role: "owner",
        pendingReferralCode: null,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
      ui: { readOnlyError: null, sessionExpired: false },
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
    expect(screen.getByText("Realism")).toBeInTheDocument();
    expect(screen.getByText("Blackwork")).toBeInTheDocument();
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
});
