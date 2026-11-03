import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { Toaster } from "sonner";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { artistsApi } from "@/features/artists/artistsApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { savedPaymentMethodsApi } from "@/features/saved-payment-methods/savedPaymentMethodsApi";
import { messagingApi } from "@/features/messaging/messagingApi";
import { ClientAppointmentDetailPage } from "@/features/appointments/components/ClientAppointmentDetailPage";
import type {
  AppointmentReadinessItem,
  AppointmentResponse,
  CancellationPreviewResponse,
  ClientAppointmentContact,
  ClientAppointmentDetailResponse,
} from "@/features/appointments/appointment.types";
import type { StudioResponse } from "@/features/studios/studiosApi";
import { Role } from "@/shared/types/roles";

// ── Seed data ──────────────────────────────────────────────────────────────────

const STUDIO: StudioResponse = {
  id: "s-001", name: "Test Studio", slug: "test-studio",
  city: "Tirana", latitude: 41.3, longitude: 19.8,
  showPlatformBranding: false, allowBrandingRemoval: false, allowApiAccess: false,
  trialExpiresAt: "2030-01-01T00:00:00Z",
  createdAt: "2024-01-01T00:00:00Z", isActive: true,
  slugLockedAt: null,
  phoneNumber: "+355 69 000 0000",
  instagramHandle: null,
  nipt: null,
  isSolo: false,
  isPublished: true,
  timezone: "Europe/Tirane",
  addressLine1: null,
  addressLine2: null,
  postalCode: null,
  countryCode: "PT",
  currency: "EUR",
  currencyLocked: false,
};

// 10 Dec 2030, 10:00–12:00 Tirana (CET, UTC+1)
const APPT: AppointmentResponse = {
  id: "appt-001", studioId: "s-001",
  artistId: "a-001", clientId: "c-001",
  date: "2030-12-10T09:00:00Z", endDate: "2030-12-10T11:00:00Z",
  durationMinutes: 120, status: "Confirmed",
  depositStatus: "Pending", depositAmount: 50,
  notes: null, createdAt: "2024-01-01T00:00:00Z",
  artistName: "Luna Artista",
  serviceName: "Half sleeve session",
  tattooDescription: "A swallow over the wrist",
  attachments: [],
  selfService: { noticeWindowHours: 48, isWithinNoticeWindow: true },
  readiness: { required: 0, satisfied: 0 },
};

function item(key: AppointmentReadinessItem["key"], satisfied: boolean, extra: Partial<AppointmentReadinessItem> = {}): AppointmentReadinessItem {
  const labels: Record<string, [string, string, string | null]> = {
    DepositPaid:    ["Deposit", "Pay your deposit", null],
    IntakeForm:     ["Intake form", "Fill in the intake form", "/forms/intake/new?appointmentId=appt-001"],
    ConsentForm:    ["Consent form", "Sign the consent form", "/forms/consent/new?appointmentId=appt-001"],
    ReferenceImage: ["Reference image", "Share a reference image", null],
  };
  const [label, actionLabel, destination] = labels[key];
  const available = !satisfied && key !== "ReferenceImage";
  return {
    key, label, actionLabel, required: true, satisfied, available,
    destination: available ? destination : null,
    note: key === "ReferenceImage" && !satisfied ? "Reference images are added when you book." : null,
    ...extra,
  };
}

const CONTACT: ClientAppointmentContact = {
  recipientUserId: "u-artist", recipientRole: "artist", recipientName: "Luna Artista",
  studioName: "Test Studio", studioPhone: "+355 69 000 0000", studioInstagram: null,
};

function detail(
  appt: Partial<AppointmentResponse> = {},
  readiness: AppointmentReadinessItem[] = [],
  contact: Partial<ClientAppointmentContact> = {},
): ClientAppointmentDetailResponse {
  return {
    contact: { ...CONTACT, ...contact },
    appointment: {
      ...APPT,
      ...appt,
      readiness: { required: readiness.length, satisfied: readiness.filter((r) => r.satisfied).length },
    },
    readiness,
  };
}

function preview(overrides: Partial<CancellationPreviewResponse> = {}): CancellationPreviewResponse {
  return {
    appointmentId: "appt-001", noticeWindowHours: 48, isWithinNoticeWindow: true, noticeWindowApplies: true,
    refundPercent: 100, depositAmount: 50, currency: "EUR", refundAmount: 0, keptAmount: 0,
    outcome: "NothingCollected", isProjection: true,
    ...overrides,
  };
}

function detailReturns(body: ClientAppointmentDetailResponse) {
  return http.get("http://localhost/api/v1/appointments/mine/:id", () => HttpResponse.json(body));
}

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json(STUDIO)),
  http.get("http://localhost/api/v1/artists", () => HttpResponse.json([])),
  http.get("http://localhost/api/v1/payments/appointment/:id", () => HttpResponse.json(null)),
  http.get("http://localhost/api/v1/saved-payment-methods", () => HttpResponse.json([])),
  http.get("http://localhost/api/v1/appointments/mine/:id", () => HttpResponse.json(detail())),
  http.get("http://localhost/api/v1/appointments/:id/cancellation-preview", () => HttpResponse.json(preview())),
  http.delete("http://localhost/api/v1/appointments/:id", () => new HttpResponse(null, { status: 204 })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      ui:   uiReducer,
      [appointmentsApi.reducerPath]:        appointmentsApi.reducer,
      [artistsApi.reducerPath]:             artistsApi.reducer,
      [studiosApi.reducerPath]:             studiosApi.reducer,
      [paymentsApi.reducerPath]:            paymentsApi.reducer,
      [savedPaymentMethodsApi.reducerPath]: savedPaymentMethodsApi.reducer,
      [messagingApi.reducerPath]:           messagingApi.reducer,
    },
    middleware: (gd) =>
      gd()
        .concat(appointmentsApi.middleware)
        .concat(artistsApi.middleware)
        .concat(studiosApi.middleware)
        .concat(paymentsApi.middleware)
        .concat(savedPaymentMethodsApi.middleware)
        .concat(messagingApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u-001", email: "test@test.com" }, token: "fake-token", tenantId: "s-001", role: Role.Client, pendingReferralCode: null, impersonation: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null, impersonationScopeError: null, impersonationSessionExpired: false },
    },
  });
}

function renderPage(id = "appt-001") {
  render(
    <Provider store={makeStore()}>
      <Toaster />
      <MemoryRouter initialEntries={[`/my-appointments/${id}`]}>
        <Routes>
          <Route path="/my-appointments/:id" element={<ClientAppointmentDetailPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

async function loaded() {
  return screen.findByRole("heading", { level: 1 });
}

// ── States ─────────────────────────────────────────────────────────────────────

describe("ClientAppointmentDetailPage — states", () => {
  it("shows a skeleton while loading", () => {
    renderPage();
    expect(screen.getByLabelText("Loading appointment")).toHaveAttribute("aria-busy", "true");
  });

  it("shows not-found for an appointment that isn't the client's (the server answers 404)", async () => {
    server.use(http.get("http://localhost/api/v1/appointments/mine/:id", () =>
      HttpResponse.json({ message: "not found" }, { status: 404 })));
    renderPage();
    expect(await screen.findByRole("heading", { name: "Appointment not found" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Go to my bookings" })).toHaveAttribute("href", "/book");
  });

  it("shows a failure with a working retry", async () => {
    let calls = 0;
    server.use(http.get("http://localhost/api/v1/appointments/mine/:id", () => {
      calls += 1;
      return calls === 1
        ? HttpResponse.json({ message: "boom" }, { status: 500 })
        : HttpResponse.json(detail());
    }));
    const user = userEvent.setup();
    renderPage();
    expect(await screen.findByText("Couldn't load this appointment")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /try again/i }));
    expect(await screen.findByRole("heading", { level: 1, name: "Half sleeve session" })).toBeInTheDocument();
  });

  it("renders the summary in full: date with weekday and year, times, duration, artist, both statuses", async () => {
    renderPage();
    await loaded();
    expect(screen.getByText(/^Tuesday,? 10 December 2030$/)).toBeInTheDocument();
    expect(screen.getByText(/10:00 – 12:00/)).toBeInTheDocument();
    expect(screen.getByText(/120 min/)).toBeInTheDocument();
    expect(screen.getByText("Luna Artista")).toBeInTheDocument();
    expect(screen.getByText("Confirmed")).toBeInTheDocument();
  });

  it("shows an unassigned artist as such, and an unknown one as unknown — never a blank", async () => {
    server.use(detailReturns(detail({ artistId: null, artistName: null })));
    renderPage();
    expect(await screen.findByText("The studio will assign an artist")).toBeInTheDocument();
    cleanup();
    server.use(detailReturns(detail({ artistId: "a-x", artistName: null })));
    renderPage();
    expect(await screen.findByText("Artist details unavailable")).toBeInTheDocument();
  });

  it("Pending: shows 'Requested', says it still needs confirming, and offers change/cancel", async () => {
    server.use(detailReturns(detail({ status: "Pending" })));
    renderPage();
    await loaded();
    expect(screen.getByText("Requested")).toBeInTheDocument();
    expect(screen.getByText(/the studio still has to confirm it/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /need to change or cancel/i })).toBeInTheDocument();
  });

  it("Confirmed: offers change/cancel", async () => {
    renderPage();
    await loaded();
    expect(screen.getByRole("button", { name: /need to change or cancel/i })).toBeInTheDocument();
  });

  it.each([
    ["Cancelled", /this appointment was cancelled/i],
    ["Completed", null],
    ["NoShow", /marked this appointment as missed/i],
  ] as const)("%s: hides change/cancel, the next step and the checklist", async (status, note) => {
    server.use(detailReturns(detail({ status }, [item("ConsentForm", false)])));
    renderPage();
    await loaded();
    if (note) expect(screen.getByText(note)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /need to change or cancel/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Next step" })).not.toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Before your appointment" })).not.toBeInTheDocument();
  });

  it("a studio-declined request says so", async () => {
    server.use(detailReturns(detail({ status: "Cancelled", cancellationReason: "StudioDeclined" })));
    renderPage();
    expect(await screen.findByText(/the studio couldn't take this request/i)).toBeInTheDocument();
  });

  it("keeps showing the appointment while a background refetch fails, with a retry", async () => {
    let calls = 0;
    server.use(http.get("http://localhost/api/v1/appointments/mine/:id", () => {
      calls += 1;
      return calls === 1 ? HttpResponse.json(detail()) : HttpResponse.json({ message: "boom" }, { status: 500 });
    }));
    const store = makeStore();
    render(
      <Provider store={store}>
        <MemoryRouter initialEntries={["/my-appointments/appt-001"]}>
          <Routes>
            <Route path="/my-appointments/:id" element={<ClientAppointmentDetailPage />} />
          </Routes>
        </MemoryRouter>
      </Provider>,
    );
    await loaded();
    store.dispatch(appointmentsApi.util.invalidateTags(["Appointment"]));
    expect(await screen.findByText(/couldn't refresh this appointment/i)).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: "Half sleeve session" })).toBeInTheDocument();
  });
});

// ── Progressive disclosure (§8 acceptance criteria) ───────────────────────────

describe("ClientAppointmentDetailPage — progressive disclosure", () => {
  it("1. exactly one primary action, for the highest-priority outstanding item, not repeated below", async () => {
    server.use(detailReturns(detail({}, [
      item("IntakeForm", false), item("ConsentForm", false), item("ReferenceImage", false),
    ])));
    renderPage();
    await loaded();

    const nextStep = screen.getByRole("region", { name: "Next step" });
    const cta = within(nextStep).getByRole("link", { name: "Fill in the intake form" });
    expect(cta).toHaveAttribute("href", "/forms/intake/new?appointmentId=appt-001");
    expect(screen.getAllByRole("link", { name: "Fill in the intake form" })).toHaveLength(1);

    const todo = screen.getByRole("list", { name: "Still to do" });
    expect(within(todo).queryByText("Fill in the intake form")).not.toBeInTheDocument();
    expect(within(todo).getByRole("link", { name: "Sign the consent form" })).toBeInTheDocument();
    // Not actionable in the app: listed with its note, never as a link.
    expect(within(todo).getByText("Share a reference image")).toBeInTheDocument();
    expect(within(todo).queryByRole("link", { name: "Share a reference image" })).not.toBeInTheDocument();
    expect(within(todo).getByText(/added when you book/i)).toBeInTheDocument();
  });

  it("1. a deposit as the next step is the one filled pay button; the summary shows only its status", async () => {
    server.use(detailReturns(detail({}, [item("DepositPaid", false), item("ConsentForm", false)])));
    renderPage();
    await loaded();
    const nextStep = screen.getByRole("region", { name: "Next step" });
    expect(await within(nextStep).findByRole("button", { name: /pay deposit/i })).toBeInTheDocument();
    expect(screen.getAllByRole("button", { name: /pay deposit/i })).toHaveLength(1);
  });

  it("1. nothing outstanding: no CTA at all (not a disabled one) and an all-set message", async () => {
    server.use(detailReturns(detail({}, [item("ConsentForm", true), item("IntakeForm", true)])));
    renderPage();
    await loaded();
    expect(screen.queryByRole("region", { name: "Next step" })).not.toBeInTheDocument();
    expect(screen.getByText("You're all set.")).toBeInTheDocument();
  });

  it("1. a service with no requirements shows no checklist at all", async () => {
    renderPage();
    await loaded();
    expect(screen.queryByRole("heading", { name: "Before your appointment" })).not.toBeInTheDocument();
    expect(screen.queryByText(/0 of 0/)).not.toBeInTheDocument();
  });

  it("2. completed items sit in a real disclosure, collapsed by default and out of the tab order", async () => {
    server.use(detailReturns(detail({}, [item("ConsentForm", true), item("IntakeForm", false)])));
    const user = userEvent.setup();
    renderPage();
    await loaded();

    const trigger = screen.getByRole("button", { name: "Done (1)" });
    expect(trigger).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("list", { name: "Done" })).not.toBeInTheDocument();

    // Tabbing through the whole page never reaches the collapsed content.
    const reached: Element[] = [];
    for (let i = 0; i < 20; i++) {
      await user.tab();
      if (document.activeElement) reached.push(document.activeElement);
    }
    expect(reached).toContain(trigger);
    expect(reached.some((el) => el.closest("[aria-label='Done']"))).toBe(false);

    // Keyboard-operable.
    trigger.focus();
    await user.keyboard("{Enter}");
    expect(trigger).toHaveAttribute("aria-expanded", "true");
    expect(within(screen.getByRole("list", { name: "Done" })).getByText("Consent form")).toBeInTheDocument();
  });

  it("3. a long description collapses behind an accessible 'Show more' that reveals all of it", async () => {
    const long = `${"Fine-line botanical piece with ".repeat(12)}THE-VERY-END`;
    server.use(detailReturns(detail({ tattooDescription: long })));
    const user = userEvent.setup();
    renderPage();
    await loaded();

    const toggle = screen.getByRole("button", { name: "Show more" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByText(/THE-VERY-END/)).not.toBeInTheDocument();

    await user.click(toggle);
    expect(screen.getByRole("button", { name: "Show less" })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByText(/THE-VERY-END/)).toBeInTheDocument();
  });

  it("3. a short description is shown whole, with no toggle", async () => {
    renderPage();
    await loaded();
    expect(screen.getByText("A swallow over the wrist")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Show more" })).not.toBeInTheDocument();
  });

  it("4. change/cancel is discoverable but collapsed, and never a filled primary button", async () => {
    server.use(detailReturns(detail({}, [item("ConsentForm", false)])));
    const user = userEvent.setup();
    renderPage();
    await loaded();
    const trigger = screen.getByRole("button", { name: /need to change or cancel/i });
    expect(trigger).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("button", { name: "Cancel appointment" })).not.toBeInTheDocument();

    await user.click(trigger);
    const cancel = screen.getByRole("button", { name: "Cancel appointment" });
    expect(cancel.className).not.toMatch(/bg-primary/);
    expect(screen.getByRole("button", { name: "Reschedule" }).className).not.toMatch(/bg-primary/);
  });

  it("5. every action works from the keyboard alone (nothing is hover-, swipe- or long-press-only)", async () => {
    server.use(detailReturns(detail({}, [item("ConsentForm", false)])));
    const user = userEvent.setup();
    renderPage();
    await loaded();
    const trigger = screen.getByRole("button", { name: /need to change or cancel/i });
    trigger.focus();
    await user.keyboard("{Enter}");
    const cancel = screen.getByRole("button", { name: "Cancel appointment" });
    cancel.focus();
    await user.keyboard("{Enter}");
    expect(screen.getByRole("alertdialog")).toBeInTheDocument();
  });

  it("6. date, deposit status, booking status, the primary action and error text render in full", async () => {
    server.use(detailReturns(detail({}, [item("DepositPaid", false)])));
    renderPage();
    await loaded();
    expect(screen.getByText(/^Tuesday,? 10 December 2030$/)).not.toHaveClass("truncate");
    expect(screen.getByText("Confirmed")).toBeInTheDocument();
    expect(screen.getByText("Pending")).toBeInTheDocument(); // deposit status badge
    const pay = await screen.findByRole("button", { name: /pay deposit/i });
    expect(pay).not.toHaveClass("truncate");
  });
});

// ── The cancel panel, exactly as slice 1 left it ──────────────────────────────

describe("ClientAppointmentDetailPage — cancel panel", () => {
  async function openCancel(user: ReturnType<typeof userEvent.setup>) {
    await loaded();
    await user.click(screen.getByRole("button", { name: /need to change or cancel/i }));
    await user.click(screen.getByRole("button", { name: "Cancel appointment" }));
  }

  it("opens as an alertdialog with focus on 'Keep appointment' and 'Yes, cancel' disabled until the preview loads", async () => {
    let release: () => void = () => {};
    const gate = new Promise<void>((r) => { release = r; });
    server.use(http.get("http://localhost/api/v1/appointments/:id/cancellation-preview", async () => {
      await gate;
      return HttpResponse.json(preview({ outcome: "FullyRefunded", refundAmount: 50 }));
    }));
    const user = userEvent.setup();
    renderPage();
    await openCancel(user);

    const dialog = screen.getByRole("alertdialog");
    expect(within(dialog).getByRole("button", { name: "Keep appointment" })).toHaveFocus();
    expect(within(dialog).getByRole("button", { name: /yes, cancel/i })).toBeDisabled();

    release();
    await waitFor(() => expect(within(dialog).getByRole("button", { name: /yes, cancel/i })).toBeEnabled());
    expect(within(dialog).getByText(/full refund/i)).toBeInTheDocument();
  });

  it("an Unknown preview keeps 'Yes, cancel' disabled for good and shows the studio's phone", async () => {
    server.use(http.get("http://localhost/api/v1/appointments/:id/cancellation-preview", () =>
      HttpResponse.json(preview({ outcome: "Unknown" }))));
    const user = userEvent.setup();
    renderPage();
    await openCancel(user);
    const dialog = screen.getByRole("alertdialog");
    expect(await within(dialog).findByRole("link", { name: STUDIO.phoneNumber! })).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: /yes, cancel/i })).toBeDisabled();
  });

  it("a failed preview keeps 'Yes, cancel' disabled and shows the studio's phone", async () => {
    server.use(http.get("http://localhost/api/v1/appointments/:id/cancellation-preview", () =>
      HttpResponse.json({ message: "boom" }, { status: 500 })));
    const user = userEvent.setup();
    renderPage();
    await openCancel(user);
    const dialog = screen.getByRole("alertdialog");
    expect(await within(dialog).findByText(/couldn't check what cancelling would mean/i)).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: /yes, cancel/i })).toBeDisabled();
  });

  it("after cancelling, the page shows the server's result, not the projection", async () => {
    let cancelled = false;
    server.use(
      http.get("http://localhost/api/v1/appointments/mine/:id", () =>
        HttpResponse.json(cancelled
          ? detail({ status: "Cancelled", depositStatus: "Refunded" })
          : detail())),
      http.delete("http://localhost/api/v1/appointments/:id", () => {
        cancelled = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    const user = userEvent.setup();
    renderPage();
    await openCancel(user);
    const yes = screen.getByRole("button", { name: /yes, cancel/i });
    await waitFor(() => expect(yes).toBeEnabled());
    await user.click(yes);

    expect(await screen.findByText(/this appointment was cancelled/i)).toBeInTheDocument();
    expect(screen.getByText("Deposit refunded")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /need to change or cancel/i })).not.toBeInTheDocument();
  });
});

// ── Contact (DEC06) ─────────────────────────────────────────────────────────

describe("ClientAppointmentDetailPage — contact", () => {
  it("sends a message to the resolved recipient, tagged with this appointment, only when Send is pressed", async () => {
    let conversationBody: unknown = null;
    let messageBody: unknown = null;
    server.use(
      http.post("http://localhost/api/v1/conversations", async ({ request }) => {
        conversationBody = await request.json();
        return HttpResponse.json({
          id: "conv-1", otherUserId: "u-artist", otherRole: "artist", otherDisplayName: "Luna Artista",
          otherAvatarUrl: null, lastMessageAt: null, lastMessagePreview: null, lastMessageFromMe: false,
          unreadCount: 0, createdAt: "2030-01-01T00:00:00Z",
        });
      }),
      http.post("http://localhost/api/v1/conversations/:id/messages", async ({ request }) => {
        messageBody = await request.json();
        return HttpResponse.json({
          id: "m-1", conversationId: "conv-1", senderUserId: "u-001", senderRole: "client",
          body: "Can I bring a friend?", createdAt: "2030-01-01T00:00:00Z", readAt: null, appointmentId: "appt-001",
        });
      }),
    );
    const user = userEvent.setup();
    renderPage();
    await loaded();

    await user.type(screen.getByLabelText("Message Luna Artista about this appointment"), "Can I bring a friend?");
    expect(conversationBody).toBeNull();
    expect(messageBody).toBeNull();

    await user.click(screen.getByRole("button", { name: "Send" }));
    expect(await screen.findByText(/message sent/i)).toBeInTheDocument();
    expect(conversationBody).toEqual({ recipientUserId: "u-artist" });
    expect(messageBody).toEqual({ body: "Can I bring a friend?", appointmentId: "appt-001" });
    expect(screen.getByRole("link", { name: "Open the conversation" })).toHaveAttribute("href", "/messages?conversation=conv-1");
  });

  it("names the studio, not a person, when the recipient is the owner", async () => {
    server.use(detailReturns(detail({ artistId: null, artistName: null }, [], {
      recipientUserId: "u-owner", recipientRole: "owner", recipientName: "Dua",
    })));
    renderPage();
    await loaded();
    expect(screen.getByLabelText("Message Test Studio about this appointment")).toBeInTheDocument();
  });

  it("with no recipient: no compose box, and the studio's contact details instead", async () => {
    server.use(detailReturns(detail({}, [], { recipientUserId: null, recipientRole: null, recipientName: null, studioInstagram: "@teststudio" })));
    renderPage();
    await loaded();
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Send" })).not.toBeInTheDocument();
    const contact = screen.getByRole("region", { name: "Contact the studio" });
    expect(within(contact).getByRole("link", { name: "+355 69 000 0000" })).toHaveAttribute("href", "tel:+355 69 000 0000");
    expect(within(contact).getByRole("link", { name: "@teststudio" })).toHaveAttribute("href", "https://instagram.com/teststudio");
  });

  it("a failed send says so and keeps the text", async () => {
    server.use(http.post("http://localhost/api/v1/conversations", () => HttpResponse.json({ message: "no" }, { status: 403 })));
    const user = userEvent.setup();
    renderPage();
    await loaded();
    const box = screen.getByLabelText("Message Luna Artista about this appointment");
    await user.type(box, "Hello");
    await user.click(screen.getByRole("button", { name: "Send" }));
    expect(await screen.findByText(/your message wasn't sent/i)).toBeInTheDocument();
    expect(box).toHaveValue("Hello");
  });
});

// ── Visit and aftercare (DEC11) ───────────────────────────────────────────────

describe("ClientAppointmentDetailPage — visit and aftercare", () => {
  const ADDRESS = { addressComplete: true, addressLine1: "Rruga e Kavajes 7", addressLine2: "Kati 2", city: "Tirana", postalCode: "1001" };

  it("Pending: no address, and says it comes once the booking is confirmed", async () => {
    server.use(detailReturns({ ...detail({ status: "Pending" }), visit: null }));
    renderPage();
    await loaded();
    expect(screen.queryByText("Rruga e Kavajes 7")).not.toBeInTheDocument();
    expect(screen.getByText(/appears here once your booking is confirmed/i)).toBeInTheDocument();
  });

  it("Confirmed: shows the studio's real address", async () => {
    server.use(detailReturns({ ...detail(), visit: ADDRESS }));
    renderPage();
    await loaded();
    const visit = screen.getByRole("region", { name: "Your visit" });
    expect(within(visit).getByText("Rruga e Kavajes 7")).toBeInTheDocument();
    expect(within(visit).getByText("Kati 2")).toBeInTheDocument();
    expect(within(visit).getByText("1001 Tirana")).toBeInTheDocument();
  });

  it("Confirmed with an incomplete address: says so and points at the studio, no partial address", async () => {
    server.use(detailReturns({
      ...detail(),
      visit: { addressComplete: false, addressLine1: null, addressLine2: null, city: null, postalCode: null },
    }));
    renderPage();
    await loaded();
    expect(screen.getByText(/hasn't added its full address yet/i)).toBeInTheDocument();
  });

  it("Completed with aftercare: shows exactly the steps the server sent, and no visit block", async () => {
    server.use(detailReturns({
      ...detail({ status: "Completed" }),
      aftercare: { sentAt: "2030-12-10T15:00:00Z", steps: ["Step from the server one.", "Step from the server two."] },
    }));
    renderPage();
    await loaded();
    const aftercare = screen.getByRole("region", { name: "Aftercare" });
    expect(within(aftercare).getAllByRole("listitem").map((li) => li.textContent)).toEqual([
      "Step from the server one.", "Step from the server two.",
    ]);
    expect(within(aftercare).getByText(/10 December 2030/)).toBeInTheDocument();
    expect(screen.queryByRole("region", { name: "Your visit" })).not.toBeInTheDocument();
  });

  it("Completed without published aftercare: no aftercare text at all", async () => {
    server.use(detailReturns({ ...detail({ status: "Completed" }), aftercare: null }));
    renderPage();
    await loaded();
    expect(screen.queryByRole("region", { name: "Aftercare" })).not.toBeInTheDocument();
  });
});
