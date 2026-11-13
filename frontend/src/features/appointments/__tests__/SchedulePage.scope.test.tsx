import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { remindersApi } from "@/features/reminders/remindersApi";
import { artistsApi } from "@/features/artists/artistsApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { SchedulePage } from "@/features/appointments/components/SchedulePage";
import type { AppointmentResponse } from "@/features/appointments/appointment.types";
import { Role } from "@/shared/types/roles";

// E05c (O03): the schedule says whose appointments it shows, and an owner can narrow it to one artist
// through the server's existing artistId filter. Its own file, so it mounts only SchedulePage.

const today = new Date();
today.setHours(10, 0, 0, 0);

function appt(over: Partial<AppointmentResponse>): AppointmentResponse {
  return {
    id: "appt-1", studioId: "s-001", artistId: "a-ana", clientId: "c-001",
    date: today.toISOString(), endDate: new Date(today.getTime() + 3_600_000).toISOString(),
    durationMinutes: 60, status: "Confirmed", depositStatus: "Pending", depositAmount: 0,
    notes: null, createdAt: "2026-01-01T00:00:00Z", artistName: "Ana Hoxha", clientName: "Client One",
    ...over,
  };
}

const ARTISTS = [
  { id: "a-ana", firstName: "Ana", lastName: "Hoxha" },
  { id: "a-bea", firstName: "Bea", lastName: "Kola" },
].map((a) => ({
  ...a, studioId: "s-001", userId: null, email: "", specializations: [], hourlyRate: null, isActive: true,
  avatarUrl: null, portfolioImages: [], slug: null, createdAt: "", updatedAt: "",
}));

/** Every GET /appointments, so a test can assert the request rather than the rendering. */
let requests: URL[] = [];
let appointments: AppointmentResponse[] = [];

const server = setupServer(
  http.get("http://localhost/api/v1/appointments", ({ request }) => {
    requests.push(new URL(request.url));
    return HttpResponse.json(appointments);
  }),
  http.get("http://localhost/api/v1/artists", () => HttpResponse.json(ARTISTS)),
  http.get("http://localhost/api/v1/reminders", () => HttpResponse.json([])),
  http.get("http://localhost/api/v1/studios/me", () =>
    HttpResponse.json({ id: "s-001", timezone: "Europe/Tirane", currency: "EUR" })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); requests = []; appointments = []; });
afterAll(() => server.close());

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{location.search}</output>;
}

function renderSchedule(role: Role, path = "/schedule") {
  const store = configureStore({
    reducer: {
      auth: authReducer,
      ui: uiReducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
      [remindersApi.reducerPath]: remindersApi.reducer,
      [artistsApi.reducerPath]: artistsApi.reducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(
      appointmentsApi.middleware, remindersApi.middleware, artistsApi.middleware, studiosApi.middleware),
    preloadedState: {
      auth: {
        user: { id: "u-001", email: "owner@example.test" }, token: "t", refreshToken: null, tenantId: "s-001",
        role, pendingReferralCode: null, impersonation: null,
      } as ReturnType<typeof authReducer>,
    },
  });
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/schedule" element={<><SchedulePage /><LocationProbe /></>} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

const lastRequest = () => requests[requests.length - 1];

describe("SchedulePage scope (E05c, O03)", () => {
  it("an artist is told it's their own schedule, gets no artist filter, and the request is unchanged", async () => {
    renderSchedule(Role.Artist);
    expect(await screen.findByTestId("schedule-scope")).toHaveTextContent(/showing your own appointments\./i);
    expect(screen.queryByLabelText(/^artist$/i)).not.toBeInTheDocument();
    await waitFor(() => expect(requests.length).toBeGreaterThan(0));
    expect(lastRequest().searchParams.has("artistId")).toBe(false);
  });

  it("a plain owner is told it's every artist's appointments, and nothing narrows the request", async () => {
    renderSchedule(Role.Owner);
    expect(await screen.findByTestId("schedule-scope")).toHaveTextContent(/every artist's appointments/i);
    expect(await screen.findByLabelText(/^artist$/i)).toHaveDisplayValue("All artists (whole studio)");
    await waitFor(() => expect(requests.length).toBeGreaterThan(0));
    expect(lastRequest().searchParams.has("artistId")).toBe(false);
  });

  it("choosing an artist sends their id as the server's artistId, puts it in the URL and names them", async () => {
    const user = userEvent.setup();
    renderSchedule(Role.Owner);
    const select = await screen.findByLabelText(/^artist$/i);
    await screen.findByRole("option", { name: "Bea Kola" });

    await user.selectOptions(select, "a-bea");

    await waitFor(() => expect(lastRequest().searchParams.get("artistId")).toBe("a-bea"));
    expect(screen.getByTestId("location")).toHaveTextContent("?artist=a-bea");
    expect(screen.getByTestId("schedule-scope")).toHaveTextContent(/bea kola.s appointments only/i);
  });

  it("a linked filter (?artist=) narrows the first request, and clearing it returns to the whole studio", async () => {
    const user = userEvent.setup();
    renderSchedule(Role.Owner, "/schedule?artist=a-ana");
    await waitFor(() => expect(requests.length).toBeGreaterThan(0));
    expect(requests[0].searchParams.get("artistId")).toBe("a-ana");
    expect(await screen.findByTestId("schedule-scope")).toHaveTextContent(/ana hoxha.s appointments only/i);
    expect(screen.getByTestId("schedule-scope")).toHaveTextContent(/requests with no artist aren.t shown here/i);

    await user.selectOptions(screen.getByLabelText(/^artist$/i), "All artists (whole studio)");

    await waitFor(() => expect(lastRequest().searchParams.has("artistId")).toBe(false));
    expect(screen.getByTestId("location")).toBeEmptyDOMElement();
  });

  it("links an owner to the dashboard's Needs you for requests with no artist, rather than counting them", async () => {
    renderSchedule(Role.Owner);
    const scope = await screen.findByTestId("schedule-scope");
    expect(within(scope).getByRole("link", { name: "Dashboard" })).toHaveAttribute("href", "/dashboard");
    expect(scope).not.toHaveTextContent(/\d/);
  });

  it("an unassigned appointment reads as Unassigned in the studio view, and an assigned one names its artist", async () => {
    appointments = [appt({ id: "x", artistId: null, artistName: null, status: "Pending" }), appt({ id: "y" })];
    renderSchedule(Role.Owner);
    await screen.findByText("2 appointments");
    const lines = screen.getAllByTestId("appointment-card-artist").map((el) => el.textContent);
    expect(lines).toEqual(["Unassigned", "Artist: Ana Hoxha"]);
  });

  it("an artist's own cards don't repeat their name", async () => {
    appointments = [appt({})];
    renderSchedule(Role.Artist);
    await screen.findByText("1 appointment");
    expect(screen.queryByTestId("appointment-card-artist")).not.toBeInTheDocument();
  });

  it("filtering to another artist is not acting as them: no quick reminder, no Availability link", async () => {
    renderSchedule(Role.Owner, "/schedule?artist=a-bea");
    await screen.findByText("No appointments this week");
    expect(screen.queryByRole("button", { name: /quick reminder/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /availability/i })).not.toBeInTheDocument();
  });

  it("an owner in their own artist view is told so, and gets no studio filter", async () => {
    renderSchedule(Role.Owner, "/schedule?artistId=my-own-artist-id");
    expect(await screen.findByTestId("schedule-scope")).toHaveTextContent(/your own appointments \(your artist view\)/i);
    expect(screen.queryByLabelText(/^artist$/i)).not.toBeInTheDocument();
    await waitFor(() => expect(lastRequest().searchParams.get("artistId")).toBe("my-own-artist-id"));
  });

  it("the export button says it covers every artist while a filter is on", async () => {
    renderSchedule(Role.Owner, "/schedule?artist=a-bea");
    expect(await screen.findByRole("button", { name: "Export CSV (all artists)" })).toBeInTheDocument();
  });
});
