import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { studiosApi } from "@/features/studios/studiosApi";
import { artistsApi } from "@/features/artists/artistsApi";
import { boothRentApi } from "@/features/booth-rent/boothRentApi";
import { BoothRentManagementPage } from "@/features/booth-rent/components/BoothRentManagementPage";
import { MyBoothRentSection } from "@/features/booth-rent/components/MyBoothRentSection";
import type { BoothRentChargeResponse, BoothRentScheduleResponse } from "@/features/booth-rent/boothRent.types";
import { Role } from "@/shared/types/roles";

// Booth-rent dates are DATE-ONLY: the API stores them as UTC midnight (from a type="date" input, then
// AddDays(7) / AddMonths(1)). They must print as that calendar date in EVERY zone. This file is the guard
// for that: the nightly zone sweep runs it under UTC+14 and UTC-11 too, and both legs must show the same
// day. West of UTC, formatting in the viewer's zone (or the studio's, if the studio is west of UTC) prints
// the day before, so the UTC-11 leg is the one that would catch either mistake. Both dates are month-ends,
// so "the day before" is also a different month: the wrong answer cannot pass as a near miss.

const SCHEDULE: BoothRentScheduleResponse = {
  id: "sch-1", studioId: "s-001", artistId: "ar-1", artistName: "Artist One",
  amountFixed: 120, frequency: "Monthly", nextChargeDate: "2026-12-01T00:00:00Z", isActive: true,
  createdAt: "2026-10-01T09:00:00Z", updatedAt: "2026-10-01T09:00:00Z",
};

const CHARGE: BoothRentChargeResponse = {
  id: "ch-1", studioId: "s-001", artistId: "ar-1", artistName: "Artist One", boothRentScheduleId: "sch-1",
  amount: 120, currency: "EUR", chargedDate: "2026-11-01T00:00:00Z", isSettled: false,
  settledAt: null, settledNote: null, createdAt: "2026-11-01T05:00:00Z",
};

const server = setupServer(
  // A studio WEST of UTC: if the dates were put through the studio's zone, they would move a day as well.
  http.get("http://localhost/api/v1/studios/me", () =>
    HttpResponse.json({ id: "s-001", timezone: "America/New_York", currency: "EUR" }),
  ),
  http.get("http://localhost/api/v1/artists", () => HttpResponse.json([])),
  http.get("http://localhost/api/v1/booth-rent/schedules", () => HttpResponse.json([SCHEDULE])),
  http.get("http://localhost/api/v1/booth-rent/charges", () => HttpResponse.json([CHARGE])),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function renderWithStore(ui: React.ReactElement, role: Role) {
  const store = configureStore({
    reducer: {
      auth: authReducer,
      ui: uiReducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
      [artistsApi.reducerPath]: artistsApi.reducer,
      [boothRentApi.reducerPath]: boothRentApi.reducer,
    },
    middleware: (gd) => gd().concat(studiosApi.middleware, artistsApi.middleware, boothRentApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "test@test.com" }, token: "fake-token", tenantId: "s-001", role, pendingReferralCode: null, impersonation: null } as any,
      ui: { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null, impersonationScopeError: null, impersonationSessionExpired: false },
    },
  });
  render(<Provider store={store}><MemoryRouter>{ui}</MemoryRouter></Provider>);
}

describe("booth-rent dates are date-only in every zone", () => {
  it("the owner's page shows the stored calendar dates, never the day before", async () => {
    renderWithStore(<BoothRentManagementPage />, Role.Owner);
    expect(await screen.findByText(/next charge 1 Dec 2026/)).toBeInTheDocument();
    // The charge renders in both the table and the mobile card.
    expect((await screen.findAllByText("1 Nov 2026")).length).toBeGreaterThan(0);
    expect(screen.queryByText(/30 Nov 2026/)).not.toBeInTheDocument();
    expect(screen.queryByText(/31 Oct 2026/)).not.toBeInTheDocument();
  });

  it("the artist's own section shows the same dates", async () => {
    renderWithStore(<MyBoothRentSection />, Role.Artist);
    expect(await screen.findByText(/next charge 1 Dec 2026/)).toBeInTheDocument();
    expect(await screen.findByText(/1 Nov 2026/)).toBeInTheDocument();
    expect(screen.queryByText(/30 Nov 2026/)).not.toBeInTheDocument();
    expect(screen.queryByText(/31 Oct 2026/)).not.toBeInTheDocument();
  });
});
