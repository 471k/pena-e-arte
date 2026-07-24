import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { studiosApi, type StudioResponse, type StudioClosureResponse } from "@/features/studios/studiosApi";
import { StudioClosuresCard } from "@/features/studios/components/StudioClosuresCard";

// ── Fixtures ─────────────────────────────────────────────────────────────────

const STUDIO: StudioResponse = {
  id:                   "studio-001",
  name:                 "Ink & Soul Studio",
  slug:                 "ink-soul-studio",
  city:                 "Lisbon",
  latitude:             38.7169,
  longitude:            -9.1395,
  showPlatformBranding: true,
  allowBrandingRemoval: false,
  trialExpiresAt:       "2099-01-01T00:00:00Z",
  createdAt:            "2025-01-01T00:00:00Z",
  isActive:             true,
  slugLockedAt:         null,
  phoneNumber:          null,
  instagramHandle:      null,
  nipt:                 null,
};

const CLOSURE: StudioClosureResponse = {
  id:        "closure-001",
  startDate: "2026-12-24T00:00:00Z",
  endDate:   "2026-12-26T00:00:00Z",
  reason:    "Christmas",
};

// ── MSW server ────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json(STUDIO)),
  http.get("http://localhost/api/v1/studios/studio-001/closures", () => HttpResponse.json([])),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ───────────────────────────────────────────────────────────────────

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
        <StudioClosuresCard />
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("StudioClosuresCard", () => {

  it("renders the card title", async () => {
    renderCard();
    expect(await screen.findByRole("heading", { name: /studio closures/i })).toBeInTheDocument();
  });

  it("shows an empty state when there are no closures", async () => {
    renderCard();
    expect(await screen.findByText(/no upcoming closures/i)).toBeInTheDocument();
  });

  it("lists existing closures with reason and date range", async () => {
    server.use(
      http.get("http://localhost/api/v1/studios/studio-001/closures", () =>
        HttpResponse.json([CLOSURE]),
      ),
    );
    renderCard();
    expect(await screen.findByText("Christmas")).toBeInTheDocument();
  });

  it("opens the add-closure form when 'Add closure' is clicked", async () => {
    const user = userEvent.setup();
    renderCard();
    await user.click(await screen.findByRole("button", { name: /add closure/i }));

    expect(await screen.findByLabelText(/start date/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/end date/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/reason/i)).toBeInTheDocument();
  });

  it("submits a new closure and refreshes the list", async () => {
    const addSpy = vi.fn();
    server.use(
      http.post("http://localhost/api/v1/studios/studio-001/closures", async ({ request }) => {
        const body = await request.json();
        addSpy(body);
        return HttpResponse.json({ id: "closure-new" }, { status: 201 });
      }),
    );

    const user = userEvent.setup();
    renderCard();
    await user.click(await screen.findByRole("button", { name: /add closure/i }));

    await user.type(screen.getByLabelText(/start date/i), "2026-12-24");
    await user.type(screen.getByLabelText(/end date/i), "2026-12-26");
    await user.type(screen.getByLabelText(/reason/i), "Christmas");
    await user.click(screen.getByRole("button", { name: /^add closure$/i }));

    await waitFor(() => expect(addSpy).toHaveBeenCalledOnce());
    expect(addSpy).toHaveBeenCalledWith(
      expect.objectContaining({ startDate: "2026-12-24", endDate: "2026-12-26", reason: "Christmas" }),
    );
  });

  it("shows a validation error when end date is before start date", async () => {
    const user = userEvent.setup();
    renderCard();
    await user.click(await screen.findByRole("button", { name: /add closure/i }));

    await user.type(screen.getByLabelText(/start date/i), "2026-12-26");
    await user.type(screen.getByLabelText(/end date/i), "2026-12-24");
    await user.type(screen.getByLabelText(/reason/i), "Christmas");
    await user.click(screen.getByRole("button", { name: /^add closure$/i }));

    expect(await screen.findByText(/end date must be on or after start date/i)).toBeInTheDocument();
  });

  it("deletes a closure when the trash button is clicked", async () => {
    const deleteSpy = vi.fn();
    server.use(
      http.get("http://localhost/api/v1/studios/studio-001/closures", () =>
        HttpResponse.json([CLOSURE]),
      ),
      http.delete("http://localhost/api/v1/studios/studio-001/closures/closure-001", () => {
        deleteSpy();
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderCard();
    await user.click(await screen.findByRole("button", { name: /remove closure: christmas/i }));

    await waitFor(() => expect(deleteSpy).toHaveBeenCalledOnce());
  });

});
