import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { artistsApi, type ArtistAvailabilityResponse } from "@/features/artists/artistsApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { AvailabilityPage } from "@/features/artists/components/AvailabilityPage";

// E04c (A06): the viewer's own hours and time off, reached from the schedule.

const ME = { id: "artist-me", firstName: "Ari", lastName: "Kola" };
const AVAILABILITY: ArtistAvailabilityResponse = { schedule: [], timeOff: [] };

const server = setupServer(
  http.get("http://localhost/api/v1/artists/me", () => HttpResponse.json(ME)),
  http.get("http://localhost/api/v1/artists/artist-me/schedule", () => HttpResponse.json(AVAILABILITY)),
  http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json({ id: "s-001", timezone: "UTC", currency: "EUR" })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function renderPage(path = "/schedule/availability") {
  const store = configureStore({
    reducer: {
      auth:                     authReducer,
      [artistsApi.reducerPath]: artistsApi.reducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(artistsApi.middleware, studiosApi.middleware),
  });
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/schedule/availability" element={<AvailabilityPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

describe("AvailabilityPage", () => {
  it("edits the viewer's own artist row, with a way back to the schedule", async () => {
    renderPage();

    expect(await screen.findByText("Working hours")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: /working hours & time off/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^add time off$/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /schedule/i })).toHaveAttribute("href", "/schedule");
  });

  it("an owner in artist view goes back to their own artist schedule", async () => {
    renderPage("/schedule/availability?artistId=artist-me");

    await screen.findByText("Working hours");
    expect(screen.getByRole("link", { name: /schedule/i })).toHaveAttribute("href", "/schedule?artistId=artist-me");
  });

  it("explains, rather than failing, when the viewer has no artist profile", async () => {
    server.use(http.get("http://localhost/api/v1/artists/me", () => HttpResponse.json({ message: "not found" }, { status: 404 })));
    renderPage();

    expect(await screen.findByText(/don't have an artist profile in this studio/i)).toBeInTheDocument();
  });
});
