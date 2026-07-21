import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { artistsApi, type ArtistAvailabilityResponse } from "@/features/artists/artistsApi";
import { ArtistScheduleEditor } from "@/features/artists/components/ArtistScheduleEditor";

const ARTIST_ID = "artist-001";

const EMPTY_AVAILABILITY: ArtistAvailabilityResponse = { schedule: [], timeOff: [] };

const MONDAY_AVAILABILITY: ArtistAvailabilityResponse = {
  schedule: [{ dayOfWeek: 1, startTime: "09:00:00", endTime: "17:00:00", isAvailable: true }],
  timeOff: [{ id: "timeoff-001", startDate: "2026-12-24T00:00:00Z", endDate: "2026-12-26T00:00:00Z", reason: "Holiday" }],
};

const server = setupServer(
  http.get(`http://localhost/api/v1/artists/${ARTIST_ID}/schedule`, () =>
    HttpResponse.json(EMPTY_AVAILABILITY),
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
    },
    middleware: (gd) => gd().concat(artistsApi.middleware),
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

    await waitFor(() => expect(addSpy).toHaveBeenCalledOnce());
    expect(addSpy).toHaveBeenCalledWith(
      expect.objectContaining({ startDate: "2026-12-24", endDate: "2026-12-26", reason: "Vacation" }),
    );
  });

});
