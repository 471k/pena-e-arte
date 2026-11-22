import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import authReducer from "@/features/auth/authSlice";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { artistsApi } from "@/features/artists/artistsApi";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { clientReferralsApi } from "@/features/client-referrals/clientReferralsApi";
import { MyBookingsPage } from "@/features/appointments/components/MyBookingsPage";

const FUTURE = new Date(Date.now() + 7 * 86_400_000).toISOString();
const FUTURE_END = new Date(Date.now() + 7 * 86_400_000 + 3_600_000).toISOString();

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json({ id: "s1", name: "Tinta", timezone: "Europe/Tirane", showPlatformBranding: false })),
  http.get("http://localhost/api/v1/artists", () => HttpResponse.json([])),
  http.get("http://localhost/api/v1/appointments/mine", () => HttpResponse.json([])),
  http.get("http://localhost/api/v1/payments/appointment/:id", () => HttpResponse.json(null)),
  http.get("http://localhost/api/v1/client-referrals/*", () => HttpResponse.json(null)),
);
beforeAll(() => server.listen({ onUnhandledRequest: "bypass" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function renderPage(tenantId: string | null = "s1") {
  const store = configureStore({
    reducer: {
      auth: authReducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
      [artistsApi.reducerPath]: artistsApi.reducer,
      [paymentsApi.reducerPath]: paymentsApi.reducer,
      [clientReferralsApi.reducerPath]: clientReferralsApi.reducer,
    },
    middleware: (gd) => gd().concat(appointmentsApi.middleware, studiosApi.middleware, artistsApi.middleware, paymentsApi.middleware, clientReferralsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "c@example.com" }, token: "t", refreshToken: null, tenantId, role: "client", pendingReferralCode: null, impersonation: null } as any,
    },
  });
  render(<Provider store={store}><MemoryRouter><MyBookingsPage /></MemoryRouter></Provider>);
}

describe("MyBookingsPage — the client's home (C01)", () => {
  it("a first-time client sees an empty state whose primary action is booking, not a bare list", async () => {
    renderPage();
    expect(await screen.findByText("No bookings yet")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Book an appointment" })).toHaveAttribute("href", "/book");
    expect(screen.getAllByRole("link", { name: "Book an appointment" })).toHaveLength(1);
  });

  it("with bookings: lists them, and booking stays one action away", async () => {
    server.use(http.get("http://localhost/api/v1/appointments/mine", () => HttpResponse.json([{
      id: "a1", studioId: "s1", artistId: null, clientId: "c1", date: FUTURE, endDate: FUTURE_END, durationMinutes: 60,
      status: "Pending", depositStatus: "None", depositAmount: 0, notes: null, createdAt: FUTURE,
    }])));
    renderPage();
    expect(await screen.findByRole("link", { name: /view appointment details/i })).toHaveAttribute("href", "/my-appointments/a1");
    expect(screen.getByRole("link", { name: "Book an appointment" })).toHaveAttribute("href", "/book");
    expect(screen.queryByText("No bookings yet")).toBeNull();
  });

  it("a client with no studio yet is sent to find one, not shown an empty list", () => {
    renderPage(null);
    expect(screen.getByText("You haven't chosen a studio yet")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Browse studios" })).toHaveAttribute("href", "/discover");
  });

  it("a failed read says so (MyBookingsSection's error), never 'No bookings yet'", async () => {
    server.use(http.get("http://localhost/api/v1/appointments/mine", () => new HttpResponse(null, { status: 500 })));
    renderPage();
    expect(await screen.findByText(/couldn.t load your bookings/i)).toBeInTheDocument();
    expect(screen.queryByText("No bookings yet")).toBeNull();
  });
});
