// S01 acceptance: a viewer in America/New_York looking at a studio in Europe/Paris sees the studio's
// week and the studio's day for every appointment, including across both daylight-saving changes.
// The viewer's zone is this process's zone; the guard below fails loudly if the runtime ignores it.
import { describe, it, expect, vi, beforeAll, beforeEach, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
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

vi.mock("@microsoft/signalr", () => {
  function HubConnectionBuilder(this: Record<string, unknown>) {
    this.withUrl = vi.fn().mockReturnValue(this);
    this.withAutomaticReconnect = vi.fn().mockReturnValue(this);
    this.configureLogging = vi.fn().mockReturnValue(this);
    this.build = vi.fn(() => ({
      on: vi.fn(), start: vi.fn().mockResolvedValue(undefined),
      invoke: vi.fn().mockResolvedValue(undefined), stop: vi.fn().mockResolvedValue(undefined),
    }));
  }
  return { HubConnectionBuilder, LogLevel: { Warning: 2 } };
});

function appt(id: string, startIso: string, notes: string): AppointmentResponse {
  return {
    id, studioId: "s-001", artistId: "a-001", clientId: "c-001",
    date: startIso, endDate: new Date(new Date(startIso).getTime() + 3_600_000).toISOString(),
    durationMinutes: 60, status: "Confirmed", depositStatus: "Pending", depositAmount: 0,
    notes, createdAt: "2026-01-01T00:00:00Z",
  };
}

let requested: { from: string | null; to: string | null }[] = [];
let appointments: AppointmentResponse[] = [];

const server = setupServer(
  http.get("http://localhost/api/v1/appointments", ({ request }) => {
    const url = new URL(request.url);
    requested.push({ from: url.searchParams.get("from"), to: url.searchParams.get("to") });
    return HttpResponse.json(appointments);
  }),
  http.get("http://localhost/api/v1/reminders", () => HttpResponse.json([])),
  http.get("http://localhost/api/v1/studios/me", () =>
    HttpResponse.json({ id: "s-001", timezone: "Europe/Paris", currency: "EUR" })),
);

// The viewer's zone is this process's zone (Node applies a TZ assignment immediately).
vi.stubEnv("TZ", "America/New_York");

beforeAll(() => {
  expect(Intl.DateTimeFormat().resolvedOptions().timeZone).toBe("America/New_York");
  server.listen({ onUnhandledRequest: "error" });
});
beforeEach(() => { requested = []; appointments = []; });
afterEach(() => { server.resetHandlers(); cleanup(); vi.useRealTimers(); });
afterAll(() => { server.close(); vi.unstubAllEnvs(); });

function at(iso: string) {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(new Date(iso));
}

function renderPage() {
  const store = configureStore({
    reducer: {
      auth: authReducer, ui: uiReducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
      [remindersApi.reducerPath]: remindersApi.reducer,
      [artistsApi.reducerPath]: artistsApi.reducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(
      appointmentsApi.middleware, remindersApi.middleware, artistsApi.middleware, studiosApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u-001", email: "t@test.com" }, token: "t", tenantId: "s-001", role: Role.Artist, pendingReferralCode: null, impersonation: null } as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      ui: { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null } as any,
    },
  });
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/"]}>
        <Routes><Route path="/" element={<SchedulePage />} /></Routes>
      </MemoryRouter>
    </Provider>,
  );
}

/** The day section whose header reads e.g. "20 Nov". */
function daySection(label: string): HTMLElement {
  const header = screen.getByText(label, { selector: "span" });
  const section = header.closest("section");
  if (!section) throw new Error(`no section for ${label}`);
  return section;
}

describe("Schedule in the studio's zone (S01)", () => {
  it("New York viewer, Paris studio: an appointment at 00:30 Paris on Fri 20 Nov is on the studio's Friday", async () => {
    at("2026-11-18T15:00:00Z"); // Wed 18 Nov: studio week Mon 16 – Sun 22 Nov
    // 23:30 UTC on Thu 19 Nov = Fri 20 Nov 00:30 in Paris = Thu 19 Nov 18:30 in New York.
    appointments = [appt("a1", "2026-11-19T23:30:00Z", "Paris Friday session")];
    renderPage();

    expect(await screen.findByText("Paris Friday session")).toBeInTheDocument();
    expect(screen.getByText(/16 Nov 2026.*22 Nov 2026/)).toBeInTheDocument();
    expect(within(daySection("20 Nov")).getByText("Paris Friday session")).toBeInTheDocument();
    expect(within(daySection("19 Nov")).queryByText("Paris Friday session")).not.toBeInTheDocument();
    // The week asked for is the studio's Monday 00:00 to the next Monday 00:00 (CET, UTC+1).
    expect(requested.at(-1)).toEqual({ from: "2026-11-15T23:00:00.000Z", to: "2026-11-22T23:00:00.000Z" });
  });

  it("marks the studio's today, which in New York is still yesterday", async () => {
    at("2026-11-19T23:30:00Z"); // Fri 20 Nov 00:30 in Paris, Thu 19 Nov 18:30 in New York
    appointments = [appt("a1", "2026-11-20T10:00:00Z", "Friday morning")];
    renderPage();
    await screen.findByText("Friday morning");
    expect(within(daySection("20 Nov")).getByText("Today")).toBeInTheDocument();
    expect(within(daySection("19 Nov")).queryByText("Today")).not.toBeInTheDocument();
  });

  it("spring forward: the week is 167 hours and Sunday 23:30 CEST stays on Sunday", async () => {
    at("2026-03-25T12:00:00Z"); // week Mon 23 – Sun 29 Mar; clocks go forward Sun 29 Mar 02:00
    appointments = [appt("a1", "2026-03-29T21:30:00Z", "Late Sunday after the change")];
    renderPage();
    await screen.findByText("Late Sunday after the change");
    expect(within(daySection("29 Mar")).getByText("Late Sunday after the change")).toBeInTheDocument();
    const { from, to } = requested.at(-1)!;
    expect(from).toBe("2026-03-22T23:00:00.000Z");
    expect(to).toBe("2026-03-29T22:00:00.000Z");
    expect((new Date(to!).getTime() - new Date(from!).getTime()) / 3_600_000).toBe(167);
  });

  it("fall back: the week is 169 hours and Sunday 23:30 CET stays on Sunday", async () => {
    at("2026-10-21T12:00:00Z"); // week Mon 19 – Sun 25 Oct; clocks go back Sun 25 Oct 03:00
    appointments = [appt("a1", "2026-10-25T22:30:00Z", "Late Sunday after fall back")];
    renderPage();
    await screen.findByText("Late Sunday after fall back");
    expect(within(daySection("25 Oct")).getByText("Late Sunday after fall back")).toBeInTheDocument();
    const { from, to } = requested.at(-1)!;
    expect(from).toBe("2026-10-18T22:00:00.000Z");
    expect(to).toBe("2026-10-25T23:00:00.000Z");
    expect((new Date(to!).getTime() - new Date(from!).getTime()) / 3_600_000).toBe(169);
  });

  it("if the studio's zone can't be loaded: an error with a retry, never an empty week, and no guessed range", async () => {
    at("2026-11-18T15:00:00Z");
    server.use(http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json({}, { status: 500 })));
    renderPage();
    expect(await screen.findByText(/couldn.t load the studio.s time zone/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /try again/i })).toBeInTheDocument();
    expect(screen.queryByText("No appointments this week")).not.toBeInTheDocument();
    expect(requested).toHaveLength(0);
  });
});
