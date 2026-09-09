import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { studiosApi, type StudioHoursEntry } from "@/features/studios/studiosApi";
import { StudioHoursCard } from "@/features/studios/components/StudioHoursCard";

const STUDIO_ID = "studio-001";

const EMPTY_HOURS: StudioHoursEntry[] = [];

const MONDAY_HOURS: StudioHoursEntry[] = [
  { dayOfWeek: 1, startTime: "09:00:00", endTime: "18:00:00", isOpen: true },
];

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me", () =>
    HttpResponse.json({ id: STUDIO_ID, timezone: "Europe/Tirane" }),
  ),
  http.get(`http://localhost/api/v1/studios/${STUDIO_ID}/hours`, () =>
    HttpResponse.json(EMPTY_HOURS),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth:                     authReducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(studiosApi.middleware),
  });
}

function renderCard() {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <StudioHoursCard />
      </MemoryRouter>
    </Provider>,
  );
}

describe("StudioHoursCard", () => {
  it("renders all 7 days of the week", async () => {
    renderCard();
    expect(await screen.findByText("Sunday")).toBeInTheDocument();
    expect(screen.getByText("Monday")).toBeInTheDocument();
    expect(screen.getByText("Saturday")).toBeInTheDocument();
  });

  it("shows a day as closed by default when there is no saved entry", async () => {
    renderCard();
    const mondaySwitch = await screen.findByRole("switch", { name: /monday open/i });
    expect(mondaySwitch).toHaveAttribute("aria-checked", "false");
  });

  it("shows a day as open with its saved hours", async () => {
    server.use(
      http.get(`http://localhost/api/v1/studios/${STUDIO_ID}/hours`, () =>
        HttpResponse.json(MONDAY_HOURS),
      ),
    );
    renderCard();
    const mondaySwitch = await screen.findByRole("switch", { name: /monday open/i });
    expect(mondaySwitch).toHaveAttribute("aria-checked", "true");
  });

  it("disables the time inputs for a closed day", async () => {
    renderCard();
    await screen.findByText("Sunday");
    const startInput = screen.getByLabelText(/sunday opening time/i);
    expect(startInput).toBeDisabled();
  });

  it("saves studio hours when a day is toggled open and Save is clicked", async () => {
    const saveSpy = vi.fn();
    server.use(
      http.put(`http://localhost/api/v1/studios/${STUDIO_ID}/hours`, async ({ request }) => {
        const body = await request.json();
        saveSpy(body);
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderCard();
    const mondaySwitch = await screen.findByRole("switch", { name: /monday open/i });
    await user.click(mondaySwitch);
    await user.click(screen.getByRole("button", { name: /save studio hours/i }));

    await waitFor(() => expect(saveSpy).toHaveBeenCalledOnce());
    const [{ entries }] = saveSpy.mock.calls[0];
    expect(entries).toContainEqual(
      expect.objectContaining({ dayOfWeek: 1, isOpen: true }),
    );
  });
});
