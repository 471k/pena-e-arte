import { describe, it, expect, beforeAll, afterEach, afterAll, beforeEach, vi } from "vitest";
import { render, screen, cleanup, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { artistsApi } from "@/features/artists/artistsApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { messagingApi } from "@/features/messaging/messagingApi";
import { TodayPage } from "@/features/today";
import type {
  AppointmentPreparationItem,
  AppointmentPreparationResponse,
  AppointmentResponse,
} from "@/features/appointments/appointment.types";
import type { StudioResponse } from "@/features/studios/studiosApi";
import { Role } from "@/shared/types/roles";

// ── Seed data ──────────────────────────────────────────────────────────────────
// "Now" is Wed 4 Nov 2026, 11:00 in Tirana (CET, UTC+1). The studio's day runs 3 Nov 23:00Z – 4 Nov 23:00Z.

const NOW = new Date("2026-11-04T10:00:00Z");

const STUDIO: StudioResponse = {
  id: "s-001", name: "Test Studio", slug: "test-studio",
  city: "Tirana", latitude: 41.3, longitude: 19.8,
  showPlatformBranding: false, allowBrandingRemoval: false, allowApiAccess: false,
  trialExpiresAt: "2030-01-01T00:00:00Z",
  createdAt: "2024-01-01T00:00:00Z", isActive: true,
  slugLockedAt: null, phoneNumber: null, instagramHandle: null, nipt: null,
  isSolo: false, isPublished: true, timezone: "Europe/Tirane",
  addressLine1: null, addressLine2: null, postalCode: null,
  countryCode: "PT", currency: "EUR", currencyLocked: false,
};

function appt(overrides: Partial<AppointmentResponse>): AppointmentResponse {
  return {
    id: "appt-1", studioId: "s-001", artistId: "a-001", clientId: "c-001",
    date: "2026-11-04T13:00:00Z", endDate: "2026-11-04T15:00:00Z",
    durationMinutes: 120, status: "Confirmed",
    depositStatus: "Pending", depositAmount: 50,
    notes: null, createdAt: "2026-10-01T00:00:00Z",
    clientName: "Mira Hoxha", clientUserId: "u-client",
    serviceName: "Half sleeve session",
    tattooDescription: "A swallow over the wrist",
    desiredPlacementLocations: ["Left wrist"],
    attachments: [{ url: "https://r2.test/ref.jpg", category: "Reference" }],
    readiness: { required: 4, satisfied: 2 },
    ...overrides,
  };
}

const AFTERNOON = appt({});
const LATE_NIGHT = appt({
  id: "appt-2", clientName: "Ardit Kola",
  date: "2026-11-04T22:00:00Z", endDate: "2026-11-05T01:00:00Z", // 23:00 – 02:00 next day
  readiness: { required: 0, satisfied: 0 }, attachments: [], desiredPlacementLocations: [],
});
const FROM_LAST_NIGHT = appt({
  id: "appt-3", clientName: "Elira Dema",
  date: "2026-11-03T21:30:00Z", endDate: "2026-11-03T23:30:00Z", // 3 Nov 22:30 – 4 Nov 00:30
  readiness: { required: 0, satisfied: 0 },
});
const ENDED_YESTERDAY = appt({
  id: "appt-4", clientName: "Not Today",
  date: "2026-11-03T15:00:00Z", endDate: "2026-11-03T16:00:00Z",
});

function item(key: AppointmentPreparationItem["key"], satisfied: boolean): AppointmentPreparationItem {
  const copy: Record<string, [string, string, string, string]> = {
    DepositPaid:    ["Deposit", "Secured", "Not paid yet", "The client can pay it from their appointment page."],
    IntakeForm:     ["Intake form", "Submitted", "Not submitted yet", "The client can fill it in from their appointment page."],
    ConsentForm:    ["Consent form", "Signed", "Not signed yet", "The client can sign it from their appointment page."],
    ReferenceImage: ["Reference image", "Added", "None added", "Clients can only add reference images when they book, so ask the client about it."],
  };
  const [label, done, outstanding, followUp] = copy[key];
  return { key, label, satisfied, state: satisfied ? done : outstanding, followUp: satisfied ? null : followUp };
}

const PREPARATION: AppointmentPreparationResponse = {
  appointmentId: "appt-1",
  items: [item("DepositPaid", true), item("IntakeForm", true), item("ConsentForm", false), item("ReferenceImage", false)],
  deposit: {
    amount: 50, status: "Paid", method: "Card", paidAt: "2026-10-02T10:00:00Z", currency: "EUR",
    refundedAmount: null, appointmentStatus: "Confirmed", referenceCurrency: null, referenceRate: null,
  },
};

let listRequests: URL[] = [];
function listReturns(body: AppointmentResponse[]) {
  return http.get("http://localhost/api/v1/appointments", ({ request }) => {
    listRequests.push(new URL(request.url));
    return HttpResponse.json(body);
  });
}

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json(STUDIO)),
  http.get("http://localhost/api/v1/artists/me", () => HttpResponse.json({ id: "a-001" })),
  listReturns([AFTERNOON, LATE_NIGHT, FROM_LAST_NIGHT, ENDED_YESTERDAY]),
  http.get("http://localhost/api/v1/appointments/:id/preparation", () => HttpResponse.json(PREPARATION)),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
beforeEach(() => {
  listRequests = [];
  // Only Date is faked, so MSW and user-event keep their real timers.
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(NOW);
});
afterEach(() => { vi.useRealTimers(); server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function makeStore(role: Role = Role.Artist) {
  return configureStore({
    reducer: {
      auth: authReducer,
      ui:   uiReducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
      [artistsApi.reducerPath]:      artistsApi.reducer,
      [studiosApi.reducerPath]:      studiosApi.reducer,
      [messagingApi.reducerPath]:    messagingApi.reducer,
    },
    middleware: (gd) =>
      gd().concat(appointmentsApi.middleware).concat(artistsApi.middleware)
        .concat(studiosApi.middleware).concat(messagingApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u-artist", email: "artist@test.com" }, token: "fake-token", tenantId: "s-001", role, pendingReferralCode: null, impersonation: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null, impersonationScopeError: null, impersonationSessionExpired: false },
    },
  });
}

function renderPage(role: Role = Role.Artist) {
  render(
    <Provider store={makeStore(role)}>
      <MemoryRouter initialEntries={["/today"]}>
        <Routes>
          <Route path="/today" element={<TodayPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

function card(clientName: string): HTMLElement {
  return screen.getByRole("article", { name: clientName });
}

// ── States ─────────────────────────────────────────────────────────────────────

describe("TodayPage — states", () => {
  it("shows a skeleton while loading", () => {
    renderPage();
    expect(screen.getByLabelText("Loading today's sessions")).toHaveAttribute("aria-busy", "true");
  });

  it("asks only for the viewer's own sessions, for the studio's day plus the night before", async () => {
    renderPage();
    await screen.findByRole("article", { name: "Mira Hoxha" });
    const url = listRequests[0];
    expect(url.searchParams.get("assignedToMe")).toBe("true");
    expect(url.searchParams.get("from")).toBe("2026-11-02T23:00:00.000Z");
    expect(url.searchParams.get("to")).toBe("2026-11-04T23:00:00.000Z");
  });

  it("shows today's sessions in time order, including one still running from last night, and nothing that ended yesterday", async () => {
    renderPage();
    await screen.findByRole("article", { name: "Mira Hoxha" });
    expect(screen.getAllByRole("article").map((a) => within(a).getByRole("heading", { level: 2 }).textContent))
      .toEqual(["Elira Dema", "Mira Hoxha", "Ardit Kola"]);
    expect(screen.queryByText("Not Today")).not.toBeInTheDocument();
    expect(screen.getByText(/^Wednesday,? 4 November 2026$/)).toBeInTheDocument();
  });

  it("empty: says so plainly and points at the schedule — no empty table", async () => {
    server.use(listReturns([ENDED_YESTERDAY]));
    renderPage();
    expect(await screen.findByText("No sessions today")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open your schedule" })).toHaveAttribute("href", "/schedule");
    expect(screen.queryByRole("article")).not.toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("a day whose sessions are all finished or cancelled lists them and says nothing is left", async () => {
    server.use(listReturns([
      appt({ id: "x1", clientName: "Done One", status: "Completed" }),
      appt({ id: "x2", clientName: "Gone Two", status: "Cancelled", date: "2026-11-04T16:00:00Z", endDate: "2026-11-04T17:00:00Z" }),
      appt({ id: "x3", clientName: "Missed Three", status: "NoShow", date: "2026-11-04T18:00:00Z", endDate: "2026-11-04T19:00:00Z" }),
    ]));
    renderPage();
    expect(await screen.findByText(/every session today is finished or cancelled/i)).toBeInTheDocument();
    expect(screen.getAllByRole("article")).toHaveLength(3);
    expect(within(card("Done One")).getByText("Completed")).toBeInTheDocument();
    expect(within(card("Gone Two")).getByText("Cancelled")).toBeInTheDocument();
    expect(within(card("Missed Three")).getByText("No Show")).toBeInTheDocument();
  });

  it("a failed request shows an error with a working retry", async () => {
    let calls = 0;
    server.use(http.get("http://localhost/api/v1/appointments", () => {
      calls += 1;
      return calls === 1 ? HttpResponse.json({ message: "boom" }, { status: 500 }) : HttpResponse.json([AFTERNOON]);
    }));
    const user = userEvent.setup();
    renderPage();
    expect(await screen.findByText("Couldn't load today's sessions.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /try again/i }));
    expect(await screen.findByRole("article", { name: "Mira Hoxha" })).toBeInTheDocument();
  });

  it("a background refetch keeps the sessions on screen", async () => {
    const store = makeStore();
    render(
      <Provider store={store}>
        <MemoryRouter initialEntries={["/today"]}><Routes><Route path="/today" element={<TodayPage />} /></Routes></MemoryRouter>
      </Provider>,
    );
    await screen.findByRole("article", { name: "Mira Hoxha" });
    store.dispatch(appointmentsApi.util.invalidateTags(["Appointment"]));
    expect(screen.getByRole("article", { name: "Mira Hoxha" })).toBeInTheDocument();
    await waitFor(() => expect(listRequests.length).toBeGreaterThan(1));
    expect(screen.getByRole("article", { name: "Mira Hoxha" })).toBeInTheDocument();
  });

  it("an owner who is also an artist gets the week view of their own work", async () => {
    server.use(listReturns([]));
    renderPage(Role.Owner);
    expect(await screen.findByText("No sessions today")).toBeInTheDocument();
    await waitFor(() =>
      expect(screen.getByRole("link", { name: "Open your schedule" })).toHaveAttribute("href", "/schedule?artistId=a-001"));
    expect(listRequests[0].searchParams.get("assignedToMe")).toBe("true");
  });
});

// ── What each session shows ────────────────────────────────────────────────────

describe("TodayPage — a session", () => {
  it("names the date and time in the studio's timezone", async () => {
    renderPage();
    const c = await screen.findByRole("article", { name: "Mira Hoxha" });
    expect(await within(c).findByText("Wed 4 Nov, 14:00 – 16:00")).toBeInTheDocument();
  });

  it("a session crossing midnight names both dates, so it can't be read as one day", async () => {
    renderPage();
    const late = await screen.findByRole("article", { name: "Ardit Kola" });
    expect(within(late).getByText("Wed 4 Nov, 23:00 – Thu 5 Nov, 02:00")).toBeInTheDocument();
    const early = card("Elira Dema");
    expect(within(early).getByText("Tue 3 Nov, 22:30 – Wed 4 Nov, 00:30")).toBeInTheDocument();
  });

  it("shows the client, status, brief, placement, references, deposit and the preparation summary", async () => {
    renderPage();
    const c = await screen.findByRole("article", { name: "Mira Hoxha" });
    expect(within(c).getByText("Confirmed")).toBeInTheDocument();
    expect(within(c).getByText("A swallow over the wrist")).toBeInTheDocument();
    expect(within(c).getByText("Placement: Left wrist")).toBeInTheDocument();
    expect(within(c).getByText("1 reference image")).toBeInTheDocument();
    expect(within(c).getByText("Pending")).toBeInTheDocument();
    expect(within(c).getByText("Preparation: 2 of 4 done")).toBeInTheDocument();
    expect(within(c).getByRole("link", { name: "Open appointment" })).toHaveAttribute("href", "/appointments/appt-1");
  });

  it("no requirements configured: no preparation line at all, never '0 of 0'", async () => {
    renderPage();
    const c = await screen.findByRole("article", { name: "Ardit Kola" });
    expect(within(c).queryByText(/Preparation:/)).not.toBeInTheDocument();
    expect(within(c).queryByText(/0 of 0/)).not.toBeInTheDocument();
    expect(within(c).getByText("No reference images")).toBeInTheDocument();
  });

  it("shows no money figure beyond the deposit's own status (earnings are out of scope)", async () => {
    renderPage();
    await screen.findByRole("article", { name: "Mira Hoxha" });
    expect(screen.queryByText(/earn|takings|payout|commission|total/i)).not.toBeInTheDocument();
  });
});

// ── Progressive disclosure (§5) ────────────────────────────────────────────────

describe("TodayPage — progressive disclosure", () => {
  it("1. the full brief and itemised preparation sit behind a real disclosure, collapsed and out of the tab order", async () => {
    const user = userEvent.setup();
    renderPage();
    const c = await screen.findByRole("article", { name: "Mira Hoxha" });
    const trigger = within(c).getByRole("button", { name: "Brief and preparation" });
    expect(trigger).toHaveAttribute("aria-expanded", "false");
    expect(within(c).queryByRole("list", { name: "Preparation" })).not.toBeInTheDocument();

    const reached: Element[] = [];
    for (let i = 0; i < 25; i++) {
      await user.tab();
      if (document.activeElement) reached.push(document.activeElement);
    }
    expect(reached).toContain(trigger);
    expect(reached.some((el) => el.closest("[aria-label='Preparation']") || el.closest("[aria-label='Brief']"))).toBe(false);
  });

  it("1. opening it from the keyboard expands the summary to the itemised preparation", async () => {
    const user = userEvent.setup();
    renderPage();
    const c = await screen.findByRole("article", { name: "Mira Hoxha" });
    const trigger = within(c).getByRole("button", { name: "Brief and preparation" });
    trigger.focus();
    await user.keyboard("{Enter}");
    expect(trigger).toHaveAttribute("aria-expanded", "true");

    const list = await within(c).findByRole("list", { name: "Preparation" });
    expect(within(list).getAllByRole("listitem")).toHaveLength(4);
    expect(within(list).getByText("Not signed yet")).toBeInTheDocument();
    expect(within(list).getByText("The client can sign it from their appointment page.")).toBeInTheDocument();
    // The deposit is described the way the owner's payment screens describe it (describeDeposit).
    expect(within(c).getByText("Paid")).toBeInTheDocument();
    expect(within(c).getByText(/received/)).toBeInTheDocument();
  });

  it("1. the preparation never appears complete while it is still loading", async () => {
    let release: () => void = () => {};
    server.use(http.get("http://localhost/api/v1/appointments/:id/preparation", async () => {
      await new Promise<void>((r) => { release = r; });
      return HttpResponse.json(PREPARATION);
    }));
    const user = userEvent.setup();
    renderPage();
    const c = await screen.findByRole("article", { name: "Mira Hoxha" });
    await user.click(within(c).getByRole("button", { name: "Brief and preparation" }));
    expect(await within(c).findByText("Checking preparation…")).toBeInTheDocument();
    expect(within(c).queryByText(/Signed|Secured|Submitted/)).not.toBeInTheDocument();
    release();
    expect(await within(c).findByRole("list", { name: "Preparation" })).toBeInTheDocument();
  });

  it("1. a preparation that can't be read says so and can be retried", async () => {
    let calls = 0;
    server.use(http.get("http://localhost/api/v1/appointments/:id/preparation", () => {
      calls += 1;
      return calls === 1 ? HttpResponse.json({ message: "boom" }, { status: 500 }) : HttpResponse.json(PREPARATION);
    }));
    const user = userEvent.setup();
    renderPage();
    const c = await screen.findByRole("article", { name: "Mira Hoxha" });
    await user.click(within(c).getByRole("button", { name: "Brief and preparation" }));
    expect(await within(c).findByText("Couldn't load the preparation details.")).toBeInTheDocument();
    await user.click(within(c).getByRole("button", { name: /try again/i }));
    expect(await within(c).findByRole("list", { name: "Preparation" })).toBeInTheDocument();
  });

  it("2. a long brief collapses behind an accessible 'Show more' that reveals all of it", async () => {
    const long = `${"Fine-line botanical piece with ".repeat(12)}THE-VERY-END`;
    server.use(listReturns([appt({ tattooDescription: long })]));
    const user = userEvent.setup();
    renderPage();
    const c = await screen.findByRole("article", { name: "Mira Hoxha" });
    await user.click(within(c).getByRole("button", { name: "Brief and preparation" }));
    const toggle = within(c).getByRole("button", { name: "Show more" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(within(c).queryByText(/THE-VERY-END/)).not.toBeInTheDocument();
    await user.click(toggle);
    expect(within(c).getByRole("button", { name: "Show less" })).toHaveAttribute("aria-expanded", "true");
    expect(within(c).getByText(/THE-VERY-END/)).toBeInTheDocument();
  });

  it("3. every action works from the keyboard alone: nothing is hover-, swipe- or long-press-only", async () => {
    const user = userEvent.setup();
    renderPage();
    const c = await screen.findByRole("article", { name: "Mira Hoxha" });
    const trigger = within(c).getByRole("button", { name: "Brief and preparation" });
    trigger.focus();
    await user.keyboard("{Enter}");
    const box = await within(c).findByLabelText("Message Mira Hoxha about this session");
    box.focus();
    await user.keyboard("Hello");
    expect(within(c).getByRole("button", { name: "Send" })).toBeEnabled();
    for (const el of c.querySelectorAll("*")) {
      const attrs = el.getAttributeNames();
      expect(attrs.some((a) => /^on(mouse|pointer|touch)/i.test(a))).toBe(false);
    }
  });

  it("4. date, time, client name, status and the primary action are never truncated", async () => {
    server.use(listReturns([appt({ clientName: "Anastasia Konstantinopoulou-Marinescu de la Vega" })]));
    renderPage();
    const c = await screen.findByRole("article", { name: "Anastasia Konstantinopoulou-Marinescu de la Vega" });
    const essentials = [
      within(c).getByRole("heading", { level: 2 }),
      await within(c).findByText("Wed 4 Nov, 14:00 – 16:00"),
      within(c).getByText("Confirmed"),
      within(c).getByRole("link", { name: "Open appointment" }),
    ];
    for (const el of essentials) {
      expect(el.className).not.toMatch(/\btruncate\b|line-clamp|text-ellipsis/);
    }
    expect(within(c).getByRole("heading", { level: 2 })).toHaveTextContent("Anastasia Konstantinopoulou-Marinescu de la Vega");
  });
});

// ── §4.2: framed for the artist, never the client's instructions ───────────────

describe("TodayPage — artist framing", () => {
  it("no artist-facing string tells the artist to perform a client action", async () => {
    server.use(http.get("http://localhost/api/v1/appointments/:id/preparation", () => HttpResponse.json({
      ...PREPARATION,
      items: [item("DepositPaid", false), item("IntakeForm", false), item("ConsentForm", false), item("ReferenceImage", false)],
    })));
    const user = userEvent.setup();
    renderPage();
    const c = await screen.findByRole("article", { name: "Mira Hoxha" });
    await user.click(within(c).getByRole("button", { name: "Brief and preparation" }));
    await within(c).findByRole("list", { name: "Preparation" });

    const text = document.body.textContent ?? "";
    for (const clientAction of ["Pay your deposit", "Pay deposit", "Fill in the intake form", "Sign the consent form", "Share a reference image"]) {
      expect(text).not.toContain(clientAction);
    }
    // Every line of the itemised list is written about the client, not addressed to them.
    for (const li of within(c).getAllByRole("listitem").filter((el) => el.closest("[aria-label='Preparation']"))) {
      expect(li.textContent).not.toMatch(/\byour?\b/i);
      expect(li.textContent).not.toMatch(/(^|:\s)(Sign|Fill|Pay|Share|Submit|Upload)\b/);
    }
    // The outstanding-item affordance is messaging the client, carrying the appointment.
    expect(within(c).getByLabelText("Message Mira Hoxha about this session")).toBeInTheDocument();
  });

  it("messages the client with the appointment attached to the thread", async () => {
    let sent: unknown = null;
    server.use(
      http.post("http://localhost/api/v1/conversations", () => HttpResponse.json({ id: "conv-1" })),
      http.post("http://localhost/api/v1/conversations/:id/messages", async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json({ id: "m-1" });
      }),
    );
    const user = userEvent.setup();
    renderPage();
    const c = await screen.findByRole("article", { name: "Mira Hoxha" });
    await user.click(within(c).getByRole("button", { name: "Brief and preparation" }));
    await user.type(await within(c).findByLabelText("Message Mira Hoxha about this session"), "Could you sign the consent form?");
    await user.click(within(c).getByRole("button", { name: "Send" }));
    expect(await within(c).findByText(/Message sent/)).toBeInTheDocument();
    expect(sent).toEqual({ body: "Could you sign the consent form?", appointmentId: "appt-1" });
  });

  it("a client with no account can't be messaged, and the page says so", async () => {
    server.use(listReturns([appt({ clientUserId: null })]));
    const user = userEvent.setup();
    renderPage();
    const c = await screen.findByRole("article", { name: "Mira Hoxha" });
    await user.click(within(c).getByRole("button", { name: "Brief and preparation" }));
    expect(await within(c).findByText(/has no TattooOS account/)).toBeInTheDocument();
    expect(within(c).queryByRole("button", { name: "Send" })).not.toBeInTheDocument();
  });

  it("a finished session offers no follow-up message", async () => {
    server.use(listReturns([appt({ status: "Completed" })]));
    const user = userEvent.setup();
    renderPage();
    const c = await screen.findByRole("article", { name: "Mira Hoxha" });
    await user.click(within(c).getByRole("button", { name: "Brief and preparation" }));
    await within(c).findByRole("list", { name: "Preparation" });
    expect(within(c).queryByLabelText(/Message .* about this session/)).not.toBeInTheDocument();
  });
});
