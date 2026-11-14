import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { waitlistApi } from "@/features/waitlist/waitlistApi";
import { WaitlistClaimPage } from "@/features/waitlist/components/WaitlistClaimPage";
import { WaitlistClaimState, type WaitlistClaimResponse } from "@/features/waitlist/waitlist.types";
import { readWaitlistClaimHandoff } from "@/features/waitlist/waitlistClaim";
import { Role } from "@/shared/types/roles";

const CLAIM_URL = "http://localhost/api/v1/public/waitlist/claim";

const OFFER: WaitlistClaimResponse = {
  state:           WaitlistClaimState.Claimable,
  studioSlug:      "ink-test",
  studioName:      "Ink Test",
  studioTimezone:  "Europe/Tirane",
  artistId:        "a-1",
  artistName:      "Luna Artista",
  slotStart:       "2030-03-05T13:00:00Z",
  durationMinutes: 90,
  windowEndsAt:    "2030-03-01T10:00:00Z",
};

let lastBody: unknown = null;
const server = setupServer();
beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { cleanup(); server.resetHandlers(); lastBody = null; });
afterAll(() => server.close());

function respondWith(body: WaitlistClaimResponse | null, status = 200) {
  server.use(http.post(CLAIM_URL, async ({ request }) => {
    lastBody = await request.json();
    return body ? HttpResponse.json(body, { status }) : HttpResponse.json({ message: "not found" }, { status });
  }));
}

function makeStore(role: Role | null) {
  return configureStore({
    reducer: { auth: authReducer, ui: uiReducer, [waitlistApi.reducerPath]: waitlistApi.reducer },
    middleware: (gd) => gd().concat(waitlistApi.middleware),
    ...(role
      ? {
          preloadedState: {
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            auth: { user: { id: "u-1", email: "c@test.com" }, token: "t", tenantId: "s-1", role, pendingReferralCode: null, impersonation: null } as any,
          },
        }
      : {}),
  });
}

/** Shows where the claim page sent the visitor, and the handoff it carried. */
function BookProbe() {
  const location = useLocation();
  const handoff = readWaitlistClaimHandoff(location.state);
  return <pre data-testid="book-probe">{JSON.stringify({ search: location.search, hash: location.hash, handoff })}</pre>;
}

function renderAt(entry: string, role: Role | null = null) {
  render(
    <Provider store={makeStore(role)}>
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route path="/waitlist/claim" element={<WaitlistClaimPage />} />
          <Route path="/book" element={<BookProbe />} />
          <Route path="/s/:slug" element={<p>studio page</p>} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

describe("WaitlistClaimPage", () => {
  it("reads the token from the fragment and posts it in the body, never in the URL", async () => {
    respondWith(OFFER);
    renderAt("/waitlist/claim#t=tok-123");

    expect(await screen.findByText("A slot you were waiting for is free.")).toBeInTheDocument();
    expect(lastBody).toEqual({ token: "tok-123" });
  });

  it("shows the offered slot in studio time with the artist and studio", async () => {
    respondWith(OFFER);
    renderAt("/waitlist/claim#t=tok-123");

    // 13:00 UTC is 14:00 in Tirane in March (CET).
    expect(await screen.findByText(/14:00/)).toBeInTheDocument();
    expect(screen.getByText(/with Luna Artista at Ink Test/)).toBeInTheDocument();
  });

  it("says plainly the slot is not held, and never implies a reservation", async () => {
    respondWith(OFFER);
    renderAt("/waitlist/claim#t=tok-123");

    expect(await screen.findByText(/isn.t held for you: whoever books it first gets it/)).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/reserved for you|held for you until|we.re holding/i);
  });

  it("'Book this slot' opens the studio's booking form with the slot, artist and token in router state", async () => {
    respondWith(OFFER);
    renderAt("/waitlist/claim#t=tok-123");

    await userEvent.click(await screen.findByRole("button", { name: "Book this slot" }));

    const probe = JSON.parse(screen.getByTestId("book-probe").textContent ?? "{}");
    expect(probe.search).toBe("?studio=ink-test");
    expect(probe.hash).toBe("");
    expect(probe.handoff).toEqual({
      token: "tok-123", artistId: "a-1", slotStart: "2030-03-05T13:00:00Z", durationMinutes: 90,
    });
  });

  it("slot taken: says someone booked first and offers another time without the slot", async () => {
    respondWith({ ...OFFER, state: WaitlistClaimState.SlotTaken });
    renderAt("/waitlist/claim#t=tok-123");

    expect(await screen.findByText("Someone booked this slot before you")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Book another time" }));
    const probe = JSON.parse(screen.getByTestId("book-probe").textContent ?? "{}");
    expect(probe.handoff.slotStart).toBeNull();
    expect(probe.handoff.token).toBe("tok-123");
  });

  it("window ended: explains the 24 hours and links to the studio, not an error", async () => {
    respondWith({ ...OFFER, state: WaitlistClaimState.WindowEnded });
    renderAt("/waitlist/claim#t=tok-123");

    expect(await screen.findByText("This offer has ended")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Go to Ink Test" })).toHaveAttribute("href", "/s/ink-test");
    expect(document.body.textContent).not.toMatch(/page not found/i);
  });

  it("booked: a guest is pointed to their email", async () => {
    respondWith({ ...OFFER, state: WaitlistClaimState.Booked });
    renderAt("/waitlist/claim#t=tok-123");

    expect(await screen.findByText("You've booked from this offer")).toBeInTheDocument();
    expect(screen.getByText("The booking details are in your email.")).toBeInTheDocument();
  });

  it("booked: a signed-in client gets a link to their bookings", async () => {
    respondWith({ ...OFFER, state: WaitlistClaimState.Booked });
    renderAt("/waitlist/claim#t=tok-123", Role.Client);

    expect(await screen.findByRole("link", { name: "See your bookings" })).toHaveAttribute("href", "/book");
  });

  it("cancelled: says they left the waitlist", async () => {
    respondWith({ ...OFFER, state: WaitlistClaimState.Cancelled });
    renderAt("/waitlist/claim#t=tok-123");

    expect(await screen.findByText("You left this waitlist")).toBeInTheDocument();
  });

  it("an invalid or old link gets its own copy, not 'Page not found'", async () => {
    respondWith(null, 404);
    renderAt("/waitlist/claim#t=bad");

    expect(await screen.findByText("This link isn't valid")).toBeInTheDocument();
    expect(document.body.textContent).not.toMatch(/page not found/i);
  });

  it("a link with no token says it's incomplete and makes no request", async () => {
    renderAt("/waitlist/claim");

    expect(await screen.findByText("This link is incomplete")).toBeInTheDocument();
    expect(lastBody).toBeNull();
  });

  it("a server error offers a retry", async () => {
    respondWith(null, 500);
    renderAt("/waitlist/claim#t=tok-123");

    expect(await screen.findByText("We couldn't load your offer")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Try again" })).toBeInTheDocument();
  });
});
