import { describe, it, expect, beforeAll, afterEach, afterAll, beforeEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { authApi } from "@/features/auth/authApi";
import { publicApi } from "@/features/public/publicApi";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { BookingDraftResumePage, RESUME_TOKEN_KEY } from "@/features/appointments/components/BookingDraftResumePage";
import { DraftRestoreNotice } from "@/features/appointments/components/DraftRestoreNotice";
import type { ResumeBookingDraftResponse } from "@/features/appointments/bookingDraft";
import { Role } from "@/shared/types/roles";

const RESUME_URL = "http://localhost/api/v1/booking-drafts/resume";
const STUDIO_URL = "http://localhost/api/v1/public/studios/ink-test";
const SWITCH_URL = "http://localhost/api/v1/auth/switch-studio";

const DRAFT = {
  payload: JSON.stringify({ intake: { tattooDescription: "A swallow" } }),
  images: [], savedAt: "2026-11-25T12:00:00Z", expiresAt: "2026-12-02T12:00:00Z",
};

let lastBody: unknown = null;
const server = setupServer();
beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
beforeEach(() => sessionStorage.clear());
afterEach(() => { cleanup(); server.resetHandlers(); lastBody = null; });
afterAll(() => server.close());

function studioIs(studioId: string) {
  server.use(http.get(STUDIO_URL, () => HttpResponse.json({ studioId, slug: "ink-test", name: "Ink Test", timezone: "Europe/Tirane" })));
}

function resumeAnswers(body: ResumeBookingDraftResponse | null, status = 200) {
  server.use(http.post(RESUME_URL, async ({ request }) => {
    lastBody = await request.json();
    return body ? HttpResponse.json(body, { status }) : HttpResponse.json({ message: "Booking draft 'from this link' was not found." }, { status });
  }));
}

function makeStore(role: Role | null, tenantId = "s-1") {
  return configureStore({
    reducer: {
      auth: authReducer, ui: uiReducer,
      [authApi.reducerPath]: authApi.reducer,
      [publicApi.reducerPath]: publicApi.reducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
    },
    middleware: (gd) => gd().concat(authApi.middleware, publicApi.middleware, appointmentsApi.middleware),
    ...(role
      ? {
          preloadedState: {
            // eslint-disable-next-line @typescript-eslint/no-explicit-any
            auth: { user: { id: "u-1", email: "c@test.com" }, token: "t", tenantId, role, pendingReferralCode: null, impersonation: null } as any,
          },
        }
      : {}),
  });
}

function WhereAmI() {
  const location = useLocation();
  return <pre data-testid="where">{location.pathname + location.search + location.hash}</pre>;
}

function renderAt(entry: string, role: Role | null = Role.Client, tenantId = "s-1") {
  render(
    <Provider store={makeStore(role, tenantId)}>
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route path="/booking-drafts/resume" element={<BookingDraftResumePage />} />
          <Route path="/book" element={<WhereAmI />} />
          <Route path="/login" element={<WhereAmI />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

const LINK = "/booking-drafts/resume?studio=ink-test#t=tok-123";

describe("BookingDraftResumePage (C05)", () => {
  it("loading: shows a placeholder while the request is opened", async () => {
    studioIs("s-1");
    server.use(http.post(RESUME_URL, async () => { await new Promise((r) => setTimeout(r, 200)); return HttpResponse.json({ outcome: "Resumed", draft: DRAFT }); }));
    renderAt(LINK);

    expect(await screen.findByRole("status", { name: "Opening your booking request" })).toBeInTheDocument();
  });

  it("success: posts the token from the fragment in the body and lands on that studio's booking form", async () => {
    studioIs("s-1");
    resumeAnswers({ outcome: "Resumed", draft: DRAFT });
    renderAt(LINK);

    expect(await screen.findByTestId("where")).toHaveTextContent("/book?studio=ink-test");
    expect(lastBody).toEqual({ token: "tok-123" });
    expect(screen.getByTestId("where")).not.toHaveTextContent("tok-123");
    expect(sessionStorage.getItem(RESUME_TOKEN_KEY)).toBeNull();
  });

  it("expired: has its own message, which is why the token is not time-limited", async () => {
    studioIs("s-1");
    resumeAnswers({ outcome: "Expired", draft: null });
    renderAt(LINK);

    expect(await screen.findByText("This link has expired")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Book at this studio" })).toHaveAttribute("href", "/book?studio=ink-test");
  });

  it("already claimed: says so and offers the booking form", async () => {
    studioIs("s-1");
    resumeAnswers({ outcome: "AlreadyClaimed", draft: DRAFT });
    renderAt(LINK);

    expect(await screen.findByText("You've already picked this request up")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Go to the booking form" })).toBeInTheDocument();
  });

  it("invalid: one neutral message for every not-found, naming no cause", async () => {
    studioIs("s-1");
    resumeAnswers(null, 404);
    renderAt(LINK);

    expect(await screen.findByText("This link can't be used")).toBeInTheDocument();
    expect(sessionStorage.getItem(RESUME_TOKEN_KEY)).toBeNull();
  });

  it("signed out: keeps the token for this tab and sends the person to sign in without it in the URL", async () => {
    renderAt(LINK, null);

    const signIn = await screen.findByRole("link", { name: "Sign in to continue" });
    expect(signIn.getAttribute("href")).toBe(`/login?redirect=${encodeURIComponent("/booking-drafts/resume?studio=ink-test")}`);
    expect(signIn.getAttribute("href")).not.toContain("tok-123");
    expect(sessionStorage.getItem(RESUME_TOKEN_KEY)).toBe("tok-123");
  });

  it("after sign-in: picks the kept token up when the fragment is gone", async () => {
    sessionStorage.setItem(RESUME_TOKEN_KEY, "tok-kept");
    studioIs("s-1");
    resumeAnswers({ outcome: "Resumed", draft: DRAFT });
    renderAt("/booking-drafts/resume?studio=ink-test");

    expect(await screen.findByTestId("where")).toHaveTextContent("/book?studio=ink-test");
    expect(lastBody).toEqual({ token: "tok-kept" });
  });

  it("a staff account: explains the link opens in a client account and claims nothing", async () => {
    renderAt(LINK, Role.Owner);

    expect(await screen.findByText("This link opens in a client account")).toBeInTheDocument();
    expect(lastBody).toBeNull();
  });

  it("a link without its token: says it is incomplete", async () => {
    renderAt("/booking-drafts/resume?studio=ink-test");

    expect(await screen.findByText("This link is incomplete")).toBeInTheDocument();
  });

  it("when the studio switch fails: does not claim, and asks to try the link again", async () => {
    studioIs("s-other");
    server.use(http.post(SWITCH_URL, () => HttpResponse.json({ message: "no" }, { status: 500 })));
    renderAt(LINK, Role.Client, "s-1");

    expect(await screen.findByText("We couldn't open your booking request")).toBeInTheDocument();
    expect(lastBody).toBeNull();
  });

  it("the restore notice it lands on names the answer that was not kept (#460's sentence, reused)", () => {
    render(<DraftRestoreNotice state={{ kind: "offer", savedAtLabel: "25 November, 13:00", onResume: () => {}, onDiscard: () => {}, busy: false }} />);

    expect(screen.getByText(/except your answer to "Anything else I should know\?"/)).toBeInTheDocument();
  });
});
