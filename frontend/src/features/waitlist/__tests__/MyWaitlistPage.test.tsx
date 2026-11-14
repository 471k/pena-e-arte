import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { waitlistApi } from "@/features/waitlist/waitlistApi";
import { MyWaitlistPage } from "@/features/waitlist/components/MyWaitlistPage";
import { WaitlistStatus, type WaitlistEntryResponse } from "@/features/waitlist/waitlist.types";

const BASE: WaitlistEntryResponse = {
  id: "w-1", studioId: "s-1", artistId: "a-1", artistName: "Luna Artista",
  clientId: "c-1", clientName: null, guestName: null, guestEmail: null, guestPhone: null,
  preferredDateFrom: "2030-03-01T00:00:00Z", preferredDateTo: "2030-03-10T00:00:00Z",
  status: WaitlistStatus.Waiting, notifiedAt: null, notes: null, createdAt: "2030-02-01T00:00:00Z",
  notifiedSlotStart: null, claimToken: null,
};

const server = setupServer();
beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { cleanup(); server.resetHandlers(); });
afterAll(() => server.close());

function renderWith(entries: WaitlistEntryResponse[]) {
  server.use(http.get("http://localhost/api/v1/waitlist/mine", () => HttpResponse.json(entries)));
  const store = configureStore({
    reducer: { auth: authReducer, ui: uiReducer, [waitlistApi.reducerPath]: waitlistApi.reducer },
    middleware: (gd) => gd().concat(waitlistApi.middleware),
  });
  render(<Provider store={store}><MemoryRouter><MyWaitlistPage /></MemoryRouter></Provider>);
}

describe("MyWaitlistPage", () => {
  it("a notified entry with an open offer gets 'Book this slot', linking to the claim page by fragment", async () => {
    renderWith([{
      ...BASE, status: WaitlistStatus.Notified, notifiedAt: "2030-03-01T09:00:00Z",
      notifiedSlotStart: "2030-03-05T13:00:00Z", claimToken: "tok-abc",
    }]);

    const link = await screen.findByRole("link", { name: /Book this slot/ });
    expect(link).toHaveAttribute("href", "/waitlist/claim#t=tok-abc");
    expect(screen.getByRole("button", { name: /Remove/ })).toBeInTheDocument();
  });

  it("its wording matches what the button does: not held, first come, 24 hours", async () => {
    renderWith([{
      ...BASE, status: WaitlistStatus.Notified, notifiedAt: "2030-03-01T09:00:00Z",
      notifiedSlotStart: "2030-03-05T13:00:00Z", claimToken: "tok-abc",
    }]);

    expect(await screen.findByText(/A slot opened up on 5 Mar 2030\./)).toBeInTheDocument();
    expect(screen.getByText(/isn.t held: whoever books first gets it/)).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/claim it within 24 hours/);
  });

  it("no token (the offer is no longer open) means no 'Book this slot'", async () => {
    renderWith([{ ...BASE, status: WaitlistStatus.Notified, notifiedAt: "2030-03-01T09:00:00Z", claimToken: null }]);

    expect(await screen.findByRole("button", { name: /Remove/ })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Book this slot/ })).not.toBeInTheDocument();
  });

  it("a waiting entry has only Remove", async () => {
    renderWith([BASE]);

    expect(await screen.findByRole("button", { name: /Remove/ })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Book this slot/ })).not.toBeInTheDocument();
  });
});
