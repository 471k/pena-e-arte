import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { render, screen, cleanup, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import {
  artistsApi, type ArtistAvailabilityResponse, type TimeOffAffectedSessionsResponse,
} from "@/features/artists/artistsApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { ArtistScheduleEditor } from "@/features/artists/components/ArtistScheduleEditor";

const ARTIST_ID = "artist-001";

const EMPTY_AVAILABILITY: ArtistAvailabilityResponse = { schedule: [], timeOff: [] };

const MONDAY_AVAILABILITY: ArtistAvailabilityResponse = {
  schedule: [{ dayOfWeek: 1, startTime: "09:00:00", endTime: "17:00:00", isAvailable: true }],
  timeOff: [{ id: "timeoff-001", startDate: "2026-12-24T00:00:00Z", endDate: "2026-12-26T00:00:00Z", reason: "Holiday" }],
};

const NO_SESSIONS: TimeOffAffectedSessionsResponse = {
  artistId: ARTIST_ID, startDate: "2026-12-24", endDate: "2026-12-26", sessions: [], isProjection: true,
};

const server = setupServer(
  http.get(`http://localhost/api/v1/artists/${ARTIST_ID}/schedule`, () =>
    HttpResponse.json(EMPTY_AVAILABILITY),
  ),
  http.get("http://localhost/api/v1/studios/me", () =>
    HttpResponse.json({ id: "s-001", timezone: "UTC", currency: "EUR" }),
  ),
  http.get(`http://localhost/api/v1/artists/${ARTIST_ID}/time-off/affected-sessions`, () =>
    HttpResponse.json(NO_SESSIONS),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth:                     authReducer,
      [artistsApi.reducerPath]: artistsApi.reducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(artistsApi.middleware, studiosApi.middleware),
  });
}

function renderEditor(canEdit = true) {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <ArtistScheduleEditor artistId={ARTIST_ID} canEdit={canEdit} />
      </MemoryRouter>
    </Provider>,
  );
}

describe("ArtistScheduleEditor", () => {

  it("renders all 7 days of the week", async () => {
    renderEditor();
    expect(await screen.findByText("Sunday")).toBeInTheDocument();
    expect(screen.getByText("Monday")).toBeInTheDocument();
    expect(screen.getByText("Saturday")).toBeInTheDocument();
  });

  it("shows a day as available with its saved hours", async () => {
    server.use(
      http.get(`http://localhost/api/v1/artists/${ARTIST_ID}/schedule`, () =>
        HttpResponse.json(MONDAY_AVAILABILITY),
      ),
    );
    renderEditor();
    const mondaySwitch = await screen.findByRole("switch", { name: /monday available/i });
    expect(mondaySwitch).toHaveAttribute("aria-checked", "true");
  });

  it("lists time off entries", async () => {
    server.use(
      http.get(`http://localhost/api/v1/artists/${ARTIST_ID}/schedule`, () =>
        HttpResponse.json(MONDAY_AVAILABILITY),
      ),
    );
    renderEditor();
    expect(await screen.findByText("Holiday")).toBeInTheDocument();
  });

  it("shows empty state when there is no time off", async () => {
    renderEditor();
    expect(await screen.findByText(/no upcoming time off/i)).toBeInTheDocument();
  });

  it("hides editing controls when canEdit is false", async () => {
    renderEditor(false);
    await screen.findByText("Sunday");
    expect(screen.queryByRole("button", { name: /save working hours/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^add time off$/i })).not.toBeInTheDocument();
  });

  it("saves working hours when the toggle is enabled and Save is clicked", async () => {
    const saveSpy = vi.fn();
    server.use(
      http.put(`http://localhost/api/v1/artists/${ARTIST_ID}/schedule`, async ({ request }) => {
        const body = await request.json();
        saveSpy(body);
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderEditor();
    const mondaySwitch = await screen.findByRole("switch", { name: /monday available/i });
    await user.click(mondaySwitch);
    await user.click(screen.getByRole("button", { name: /save working hours/i }));

    await waitFor(() => expect(saveSpy).toHaveBeenCalledOnce());
    const [{ entries }] = saveSpy.mock.calls[0];
    expect(entries).toContainEqual(
      expect.objectContaining({ dayOfWeek: 1, isAvailable: true }),
    );
  });

  it("adds a time-off entry", async () => {
    const addSpy = vi.fn();
    server.use(
      http.post(`http://localhost/api/v1/artists/${ARTIST_ID}/time-off`, async ({ request }) => {
        const body = await request.json();
        addSpy(body);
        return HttpResponse.json({ id: "timeoff-new" }, { status: 201 });
      }),
    );

    const user = userEvent.setup();
    renderEditor();
    await user.click(await screen.findByRole("button", { name: /^add time off$/i }));

    await user.type(screen.getByLabelText(/start date/i), "2026-12-24");
    await user.type(screen.getByLabelText(/end date/i), "2026-12-26");
    await user.type(screen.getByLabelText(/reason/i), "Vacation");
    await user.click(screen.getByRole("button", { name: /^add time off$/i }));

    // E04c (A06): nothing is saved until the affected-session check has been shown and confirmed.
    const dialog = await screen.findByRole("alertdialog");
    expect(addSpy).not.toHaveBeenCalled();
    await within(dialog).findByText("No booked sessions fall inside these dates.");
    await user.click(within(dialog).getByRole("button", { name: /^add time off$/i }));

    await waitFor(() => expect(addSpy).toHaveBeenCalledOnce());
    expect(addSpy).toHaveBeenCalledWith(
      expect.objectContaining({ startDate: "2026-12-24", endDate: "2026-12-26", reason: "Vacation" }),
    );
  });

});

// ── E04c (A06): time off shows the sessions it covers, and never acts on them ───

const TWO_SESSIONS: TimeOffAffectedSessionsResponse = {
  artistId: ARTIST_ID, startDate: "2026-12-24", endDate: "2026-12-26", isProjection: true,
  sessions: [
    { appointmentId: "appt-1", date: "2026-12-24T10:00:00Z", endDate: "2026-12-24T12:00:00Z", clientName: "Mira Hoxha", status: "Confirmed" },
    { appointmentId: "appt-2", date: "2026-12-26T23:00:00Z", endDate: "2026-12-27T01:00:00Z", clientName: "Arben Dervishi", status: "Pending" },
  ],
};

async function openAddDialog(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("button", { name: /^add time off$/i }));
  await user.type(screen.getByLabelText(/start date/i), "2026-12-24");
  await user.type(screen.getByLabelText(/end date/i), "2026-12-26");
  await user.type(screen.getByLabelText(/reason/i), "Vacation");
  await user.click(screen.getByRole("button", { name: /^add time off$/i }));
  return screen.findByRole("alertdialog");
}

describe("ArtistScheduleEditor — affected sessions (E04c)", () => {
  it("lists every affected session in full (date, time, client, status), each linking to it", async () => {
    let asked: URLSearchParams | undefined;
    server.use(http.get(`http://localhost/api/v1/artists/${ARTIST_ID}/time-off/affected-sessions`, ({ request }) => {
      asked = new URL(request.url).searchParams;
      return HttpResponse.json(TWO_SESSIONS);
    }));
    const user = userEvent.setup();
    renderEditor();
    const dialog = await openAddDialog(user);

    const list = await within(dialog).findByRole("list", { name: /booked sessions inside these dates/i });
    const items = within(list).getAllByRole("listitem");
    expect(items).toHaveLength(2);
    expect(within(items[0]).getByRole("link", { name: "Thu 24 Dec, 10:00 – 12:00" })).toHaveAttribute("href", "/appointments/appt-1");
    expect(within(items[0]).getByText("Mira Hoxha")).toBeInTheDocument();
    expect(within(items[0]).getByText("Confirmed")).toBeInTheDocument();
    // Crosses midnight: both dates shown.
    expect(within(items[1]).getByRole("link", { name: "Sat 26 Dec, 23:00 – Sun 27 Dec, 01:00" })).toBeInTheDocument();
    expect(within(items[1]).getByText("Requested")).toBeInTheDocument();
    expect(within(dialog).getByText(/won't move or cancel them/i)).toBeInTheDocument();
    expect(asked?.get("startDate")).toBe("2026-12-24");
    expect(asked?.get("endDate")).toBe("2026-12-26");
  });

  it("offers no bulk action: the only buttons are going back and adding the time off", async () => {
    server.use(http.get(`http://localhost/api/v1/artists/${ARTIST_ID}/time-off/affected-sessions`, () => HttpResponse.json(TWO_SESSIONS)));
    const user = userEvent.setup();
    renderEditor();
    const dialog = await openAddDialog(user);
    await within(dialog).findByText("Mira Hoxha");

    const names = within(dialog).getAllByRole("button").map((b) => b.textContent?.trim());
    expect(names).toEqual(["Back", "Add time off"]);
    expect(within(dialog).queryByRole("button", { name: /cancel all|reschedule all|resolve|move/i })).not.toBeInTheDocument();
  });

  it("keeps confirm disabled until the check has resolved", async () => {
    let release: () => void = () => {};
    const gate = new Promise<void>((resolve) => { release = resolve; });
    server.use(http.get(`http://localhost/api/v1/artists/${ARTIST_ID}/time-off/affected-sessions`, async () => {
      await gate;
      return HttpResponse.json(TWO_SESSIONS);
    }));
    const user = userEvent.setup();
    renderEditor();
    const dialog = await openAddDialog(user);

    expect(within(dialog).getByText(/checking which booked sessions/i)).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: /^add time off$/i })).toBeDisabled();
    release();
    await within(dialog).findByText("Mira Hoxha");
    expect(within(dialog).getByRole("button", { name: /^add time off$/i })).toBeEnabled();
  });

  it("if the check fails, says so and does not let the time off be added blind", async () => {
    const addSpy = vi.fn();
    server.use(
      http.get(`http://localhost/api/v1/artists/${ARTIST_ID}/time-off/affected-sessions`, () =>
        HttpResponse.json({ message: "boom" }, { status: 500 })),
      http.post(`http://localhost/api/v1/artists/${ARTIST_ID}/time-off`, () => { addSpy(); return HttpResponse.json({ id: "x" }, { status: 201 }); }),
    );
    const user = userEvent.setup();
    renderEditor();
    const dialog = await openAddDialog(user);

    expect(await within(dialog).findByText(/couldn't check which booked sessions/i)).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: /^add time off$/i })).toBeDisabled();
    expect(within(dialog).getByRole("button", { name: /try again/i })).toBeInTheDocument();
    expect(addSpy).not.toHaveBeenCalled();
  });

  it("removing time off shows the same check, and removes only after confirming", async () => {
    const deleteSpy = vi.fn();
    server.use(
      http.get(`http://localhost/api/v1/artists/${ARTIST_ID}/schedule`, () => HttpResponse.json(MONDAY_AVAILABILITY)),
      http.get(`http://localhost/api/v1/artists/${ARTIST_ID}/time-off/affected-sessions`, () => HttpResponse.json(TWO_SESSIONS)),
      http.delete(`http://localhost/api/v1/artists/${ARTIST_ID}/time-off/:timeOffId`, ({ params }) => {
        deleteSpy(params.timeOffId);
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const user = userEvent.setup();
    renderEditor();
    await user.click(await screen.findByRole("button", { name: /remove time off: holiday/i }));

    const dialog = await screen.findByRole("alertdialog");
    expect(within(dialog).getByText(/24 Dec 2026 – 26 Dec 2026/)).toBeInTheDocument();
    expect(await within(dialog).findByText(/stay exactly as they are/i)).toBeInTheDocument();
    expect(within(dialog).getAllByRole("listitem")).toHaveLength(2);
    expect(deleteSpy).not.toHaveBeenCalled();
    await user.click(within(dialog).getByRole("button", { name: /^remove time off$/i }));
    await waitFor(() => expect(deleteSpy).toHaveBeenCalledWith("timeoff-001"));
  });
});
