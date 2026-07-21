import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { Toaster } from "sonner";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { artistsApi } from "@/features/artists/artistsApi";
import { clientsApi } from "@/features/clients/clientsApi";
import { depositRulesApi } from "@/features/deposit-rules/depositRulesApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { publicApi } from "@/features/public/publicApi";
import { authApi } from "@/features/auth/authApi";

import { BookPage } from "@/features/appointments/components/BookPage";
import { BookAppointmentForm } from "@/features/appointments/components/BookAppointmentForm";
import { MyBookingsSection } from "@/features/appointments/components/MyBookingsSection";

import type { ArtistResponse } from "@/features/artists/artistsApi";
import type { ClientResponse } from "@/features/clients/clientsApi";
import type { DepositRuleResponse } from "@/features/deposit-rules/depositRule.types";
import type { AppointmentResponse } from "@/features/appointments/appointment.types";
import type { StudioResponse } from "@/features/studios/studiosApi";
import { Role } from "@/shared/types/roles";

// ── Seed data ──────────────────────────────────────────────────────────────────

const STUDIO: StudioResponse = {
  id: "s-001", name: "Test Studio", slug: "test-studio",
  city: "Tirana", latitude: 41.3, longitude: 19.8,
  showPlatformBranding: false, allowBrandingRemoval: false,
  trialExpiresAt: "2030-01-01T00:00:00Z",
  createdAt: "2024-01-01T00:00:00Z", isActive: true,
  slugLockedAt: null,
  phoneNumber: null,
  instagramHandle: null,
};

const ARTIST: ArtistResponse = {
  id: "a-001", studioId: "s-001",
  firstName: "Luna", lastName: "Artista",
  email: "luna@studio.test", specializations: "Neo-trad",
  hourlyRate: 80, portfolioImages: [],
  isActive: true,
  avatarUrl: null,
  slug: null,
  userId:    null,
  createdAt: "2024-01-01T00:00:00Z",
  updatedAt: "2024-01-01T00:00:00Z",
};

const MY_CLIENT: ClientResponse = {
  id: "c-001", studioId: "s-001",
  firstName: "Marco", lastName: "Cliente",
  email: "marco@test.com", phone: null,
  createdAt: "2024-01-01T00:00:00Z", userId: "u-001",
};

const STAFF_CLIENT: ClientResponse = {
  ...MY_CLIENT, id: "c-002", firstName: "Besa", lastName: "Klienti",
  email: "besa@test.com",
};

const ACTIVE_RULE: DepositRuleResponse = {
  id: "dr-001", studioId: "s-001",
  name: "Standard (€50)", amountFixed: 50, amountPercent: null,
  isActive: true, createdAt: "2024-01-01T00:00:00Z",
  updatedAt: "2024-01-01T00:00:00Z",
  cancellationWindowHours: null, refundPercentOnLateCancel: 0,
};

const INACTIVE_RULE: DepositRuleResponse = {
  id: "dr-002", studioId: "s-001",
  name: "Old rule", amountFixed: 100, amountPercent: null,
  isActive: false, createdAt: "2024-01-01T00:00:00Z",
  updatedAt: "2024-01-01T00:00:00Z",
  cancellationWindowHours: null, refundPercentOnLateCancel: 0,
};

const FUTURE = new Date(Date.now() + 7 * 86_400_000).toISOString();
const FUTURE_END = new Date(Date.now() + 7 * 86_400_000 + 3_600_000).toISOString();
const PAST = new Date(Date.now() - 7 * 86_400_000).toISOString();
const PAST_END = new Date(Date.now() - 7 * 86_400_000 + 3_600_000).toISOString();

const APPT_UPCOMING: AppointmentResponse = {
  id: "appt-001", studioId: "s-001",
  artistId: "a-001", clientId: "c-001",
  date: FUTURE, endDate: FUTURE_END,
  durationMinutes: 60, status: "Pending",
  depositStatus: "Pending", depositAmount: 0,
  notes: null, createdAt: "2024-01-01T00:00:00Z",
};

const APPT_PAST: AppointmentResponse = {
  id: "appt-002", studioId: "s-001",
  artistId: "a-001", clientId: "c-001",
  date: PAST, endDate: PAST_END,
  durationMinutes: 60, status: "Completed",
  depositStatus: "Paid", depositAmount: 0,
  notes: null, createdAt: "2024-01-01T00:00:00Z",
};

const CREATED_APPT: AppointmentResponse = {
  id: "appt-new", studioId: "s-001",
  artistId: "a-001", clientId: "c-001",
  date: FUTURE, endDate: FUTURE_END,
  durationMinutes: 60, status: "Pending",
  depositStatus: "Pending", depositAmount: 0,
  notes: null, createdAt: "2024-01-01T00:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me",              () => HttpResponse.json(STUDIO)),
  http.get("http://localhost/api/v1/artists",                 () => HttpResponse.json([ARTIST])),
  http.get("http://localhost/api/v1/clients/me",              () => HttpResponse.json(MY_CLIENT)),
  http.get("http://localhost/api/v1/clients",                 () => HttpResponse.json([MY_CLIENT, STAFF_CLIENT])),
  http.get("http://localhost/api/v1/deposit-rules",           () => HttpResponse.json([ACTIVE_RULE, INACTIVE_RULE])),
  http.get("http://localhost/api/v1/appointments/mine",       () => HttpResponse.json([])),
  http.get("http://localhost/api/v1/appointments/check-slot", () => HttpResponse.json({ available: true, reason: null })),
  http.post("http://localhost/api/v1/appointments",           () => HttpResponse.json(CREATED_APPT, { status: 201 })),
  http.delete("http://localhost/api/v1/appointments/:id",     () => new HttpResponse(null, { status: 204 })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

// eslint-disable-next-line @typescript-eslint/no-explicit-any
function makeStore(role: Role = Role.Client) {
  return configureStore({
    reducer: {
      auth:                              authReducer,
      ui:                                uiReducer,
      [appointmentsApi.reducerPath]:     appointmentsApi.reducer,
      [artistsApi.reducerPath]:          artistsApi.reducer,
      [clientsApi.reducerPath]:          clientsApi.reducer,
      [depositRulesApi.reducerPath]:     depositRulesApi.reducer,
      [studiosApi.reducerPath]:          studiosApi.reducer,
      [paymentsApi.reducerPath]:         paymentsApi.reducer,
      [publicApi.reducerPath]:           publicApi.reducer,
      [authApi.reducerPath]:             authApi.reducer,
    },
    middleware: (gd) =>
      gd()
        .concat(appointmentsApi.middleware)
        .concat(artistsApi.middleware)
        .concat(clientsApi.middleware)
        .concat(depositRulesApi.middleware)
        .concat(studiosApi.middleware)
        .concat(paymentsApi.middleware)
        .concat(publicApi.middleware)
        .concat(authApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u-001", email: "test@test.com" }, token: "fake-token", tenantId: "s-001", role, pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null },
    },
  });
}

function renderBookPage(role: Role = Role.Client) {
  render(
    <Provider store={makeStore(role)}>
      <MemoryRouter>
        <BookPage />
      </MemoryRouter>
    </Provider>,
  );
}

function renderForm(role: Role = Role.Client) {
  render(
    <Provider store={makeStore(role)}>
      <MemoryRouter>
        <BookAppointmentForm />
      </MemoryRouter>
    </Provider>,
  );
}

// A studio-less client (signed up with no studio, never booked anywhere) has no
// active tenant — simulates that state to test the "browse studios" empty state.
function renderFormWithNoTenant() {
  const store = configureStore({
    reducer: {
      auth:                          authReducer,
      ui:                            uiReducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
      [artistsApi.reducerPath]:      artistsApi.reducer,
      [clientsApi.reducerPath]:      clientsApi.reducer,
      [depositRulesApi.reducerPath]: depositRulesApi.reducer,
      [studiosApi.reducerPath]:      studiosApi.reducer,
      [paymentsApi.reducerPath]:     paymentsApi.reducer,
      [publicApi.reducerPath]:       publicApi.reducer,
      [authApi.reducerPath]:         authApi.reducer,
    },
    middleware: (gd) =>
      gd()
        .concat(appointmentsApi.middleware)
        .concat(artistsApi.middleware)
        .concat(clientsApi.middleware)
        .concat(depositRulesApi.middleware)
        .concat(studiosApi.middleware)
        .concat(paymentsApi.middleware)
        .concat(publicApi.middleware)
        .concat(authApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u-001", email: "test@test.com" }, token: "fake-token", tenantId: null, role: Role.Client, pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null },
    },
  });
  render(
    <Provider store={store}>
      <MemoryRouter>
        <BookAppointmentForm />
      </MemoryRouter>
    </Provider>,
  );
}

function renderMyBookings(role: Role = Role.Client) {
  render(
    <Provider store={makeStore(role)}>
      <Toaster />
      <MemoryRouter>
        <MyBookingsSection />
      </MemoryRouter>
    </Provider>,
  );
}

// ── BookPage layout ────────────────────────────────────────────────────────────

describe("BookPage", () => {
  it("renders the 'Book an appointment' page heading", () => {
    renderBookPage();
    expect(screen.getByRole("heading", { name: /book an appointment/i })).toBeInTheDocument();
  });

  it("renders descriptive subtitle about the request flow", () => {
    renderBookPage();
    expect(screen.getByText(/the studio will confirm within 24 hours/i)).toBeInTheDocument();
  });

  it("renders a back navigation link", () => {
    renderBookPage();
    expect(screen.getByRole("link", { name: /back/i })).toBeInTheDocument();
  });

  it("renders the 'My bookings' section", () => {
    renderBookPage();
    expect(screen.getByText("My bookings")).toBeInTheDocument();
  });
});

// ── BookAppointmentForm ────────────────────────────────────────────────────────

describe("BookAppointmentForm", () => {
  it("shows a 'browse studios' empty state for a studio-less client with no active tenant", () => {
    renderFormWithNoTenant();
    expect(
      screen.getByText(/haven.t joined a studio yet/i),
    ).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /browse studios/i })).toHaveAttribute(
      "href", "/discover",
    );
    expect(screen.queryByLabelText("Select artist")).not.toBeInTheDocument();
  });

  it("renders the Artist label and selector", async () => {
    renderForm();
    expect(screen.getByLabelText("Select artist")).toBeInTheDocument();
    expect(await screen.findByText("Luna Artista")).toBeInTheDocument();
  });

  it("renders the Date & Time field", () => {
    renderForm();
    expect(screen.getByLabelText(/date.*time/i)).toBeInTheDocument();
  });

  it("Duration field is a select not a number input", async () => {
    renderForm();
    await screen.findByText("Luna Artista");
    expect(screen.getByRole("combobox", { name: /appointment duration/i })).toBeInTheDocument();
  });

  it("Duration select default shows '1 hour'", async () => {
    renderForm();
    await screen.findByText("Luna Artista");
    const durationSelect = screen.getByRole("combobox", { name: /appointment duration/i });
    expect(durationSelect).toHaveTextContent(/1 hour/i);
  });

  it("Duration select shows preset durations including '1 hour'", async () => {
    const user = userEvent.setup();
    renderForm();
    await screen.findByText("Luna Artista");
    await user.click(screen.getByRole("combobox", { name: /appointment duration/i }));
    expect(await screen.findByRole("option", { name: /1 hour/i })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: /30 min/i })).toBeInTheDocument();
  });

  it("renders the Notes field", () => {
    renderForm();
    expect(screen.getByLabelText(/notes/i)).toBeInTheDocument();
  });

  it("Notes placeholder provides helpful guidance", async () => {
    renderForm();
    expect(screen.getByPlaceholderText(/style.*size.*placement/i)).toBeInTheDocument();
  });

  it("renders the 'Request Appointment' submit button", () => {
    renderForm();
    expect(screen.getByRole("button", { name: /request appointment/i })).toBeInTheDocument();
  });

  it("helper text below button explains 24-hour confirmation", async () => {
    renderForm();
    await screen.findByText("Luna Artista");
    expect(screen.getByText(/artist will confirm.*24 hours/i)).toBeInTheDocument();
  });

  it("required fields have required asterisk in their label", async () => {
    renderForm();
    await screen.findByText("Luna Artista");
    const artistLabel = screen.getByText("Artist", { selector: "label" });
    expect(artistLabel.textContent).toContain("*");
  });

  it("does NOT render the Client selector for client role", async () => {
    renderForm(Role.Client);
    await screen.findByText("Luna Artista");
    expect(screen.queryByLabelText("Client")).not.toBeInTheDocument();
  });

  it("renders the Client selector for issuer (staff) role", async () => {
    renderForm(Role.Issuer);
    // Client selector is a combobox labelled "Client" (accessible name from the associated label)
    expect(await screen.findByRole("combobox", { name: /client/i })).toBeInTheDocument();
  });

  it("renders the Deposit rule selector when active rules exist", async () => {
    renderForm();
    expect(await screen.findByLabelText("Deposit rule")).toBeInTheDocument();
  });

  it("shows only active deposit rules in the selector", async () => {
    const user = userEvent.setup();
    renderForm();
    await screen.findByLabelText("Deposit rule");
    await user.click(screen.getByLabelText("Deposit rule"));
    expect(await screen.findByRole("option", { name: "Standard (€50)" })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: "Old rule" })).not.toBeInTheDocument();
  });

  it("shows 'No deposit' as the first option in the deposit rule selector", async () => {
    const user = userEvent.setup();
    renderForm();
    await screen.findByLabelText("Deposit rule");
    await user.click(screen.getByLabelText("Deposit rule"));
    expect(await screen.findByRole("option", { name: "No deposit" })).toBeInTheDocument();
  });

  it("does NOT render Deposit rule selector when no rules exist", async () => {
    server.use(
      http.get("http://localhost/api/v1/deposit-rules", () => HttpResponse.json([])),
    );
    renderForm();
    await screen.findByText("Luna Artista");
    expect(screen.queryByLabelText("Deposit rule")).not.toBeInTheDocument();
  });

  it("artist dropdown includes a search input", async () => {
    const user = userEvent.setup();
    renderForm();
    await screen.findByText("Luna Artista");
    await user.click(screen.getByLabelText("Select artist"));
    expect(screen.getByPlaceholderText(/search artists/i)).toBeInTheDocument();
  });

  it("artist dropdown items include artist specializations", async () => {
    const user = userEvent.setup();
    renderForm();
    await screen.findByText("Luna Artista");
    await user.click(screen.getByLabelText("Select artist"));
    expect(await screen.findByText("Neo-trad")).toBeInTheDocument();
  });

  it("artist search filters by name", async () => {
    server.use(
      http.get("http://localhost/api/v1/artists", () =>
        HttpResponse.json([
          ARTIST,
          { ...ARTIST, id: "a-002", firstName: "Marco", lastName: "Rivera",
            specializations: "Blackwork" },
        ]),
      ),
    );
    const user = userEvent.setup();
    renderForm();
    await screen.findByText("Luna Artista");
    await user.click(screen.getByLabelText("Select artist"));
    await user.type(screen.getByPlaceholderText(/search artists/i), "marco");
    // Use role query to avoid matching the hidden native <option> that Radix renders alongside visible items
    expect(screen.getByRole("option", { name: "Marco Rivera" })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: "Luna Artista" })).not.toBeInTheDocument();
  });

  it("shows validation error when submitted without an artist", async () => {
    const user = userEvent.setup();
    renderForm();
    await screen.findByText("Luna Artista");
    await user.click(screen.getByRole("button", { name: /request appointment/i }));
    const errorP = await screen.findByText("Select an artist", { selector: "p" });
    expect(errorP).toBeInTheDocument();
  });

  it("shows 'This slot is available' indicator after artist + date + duration are set", async () => {
    const user = userEvent.setup();
    renderForm();

    await screen.findByText("Luna Artista");
    await user.click(screen.getByLabelText("Select artist"));
    await user.click(await screen.findByRole("option", { name: "Luna Artista" }));

    const futureDate = new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 16);
    await user.type(screen.getByLabelText(/date.*time/i), futureDate);

    expect(await screen.findByText(/this slot is available/i)).toBeInTheDocument();
  });

  it("shows unavailable reason when slot check returns false", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments/check-slot", () =>
        HttpResponse.json({ available: false, reason: "That slot is already booked." }),
      ),
    );
    const user = userEvent.setup();
    renderForm();

    await screen.findByText("Luna Artista");
    await user.click(screen.getByLabelText("Select artist"));
    await user.click(await screen.findByRole("option", { name: "Luna Artista" }));

    const futureDate = new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 16);
    await user.type(screen.getByLabelText(/date.*time/i), futureDate);

    expect(await screen.findByText(/that slot is already booked/i)).toBeInTheDocument();
  });

  it("submit button is disabled when slot is unavailable", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments/check-slot", () =>
        HttpResponse.json({ available: false, reason: "Slot already booked." }),
      ),
    );
    const user = userEvent.setup();
    renderForm();

    await screen.findByText("Luna Artista");
    await user.click(screen.getByLabelText("Select artist"));
    await user.click(await screen.findByRole("option", { name: "Luna Artista" }));
    await user.type(
      screen.getByLabelText(/date.*time/i),
      new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 16),
    );

    await screen.findByText(/slot already booked/i);
    expect(screen.getByRole("button", { name: /request appointment/i }))
      .toBeDisabled();
  });

  it("shows confirmation after successful submit with no deposit", async () => {
    const user = userEvent.setup();
    renderForm();

    await screen.findByText("Luna Artista");
    await user.click(screen.getByLabelText("Select artist"));
    await user.click(await screen.findByRole("option", { name: "Luna Artista" }));

    const futureDate = new Date(Date.now() + 7 * 86_400_000);
    const formatted = futureDate.toISOString().slice(0, 16);
    await user.type(screen.getByLabelText(/date.*time/i), formatted);

    await user.click(screen.getByRole("button", { name: /request appointment/i }));
    expect(await screen.findByText("Appointment requested!")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /book another/i })).toBeInTheDocument();
  });

  it("shows deposit step after submit when appointment has a deposit", async () => {
    server.use(
      http.post("http://localhost/api/v1/appointments", () =>
        HttpResponse.json({ ...CREATED_APPT, depositAmount: 50 }, { status: 201 }),
      ),
    );
    const user = userEvent.setup();
    renderForm(Role.Client);

    await screen.findByText("Luna Artista");
    await user.click(screen.getByLabelText("Select artist"));
    await user.click(await screen.findByRole("option", { name: "Luna Artista" }));

    const futureDate = new Date(Date.now() + 7 * 86_400_000);
    await user.type(screen.getByLabelText(/date.*time/i), futureDate.toISOString().slice(0, 16));

    await user.click(screen.getByRole("button", { name: /request appointment/i }));
    expect(await screen.findByText(/secure your slot with a deposit/i)).toBeInTheDocument();
    expect(screen.getByText("€50.00")).toBeInTheDocument();
  });

  it("skipping deposit shows final confirmation", async () => {
    server.use(
      http.post("http://localhost/api/v1/appointments", () =>
        HttpResponse.json({ ...CREATED_APPT, depositAmount: 50 }, { status: 201 }),
      ),
    );
    const user = userEvent.setup();
    renderForm(Role.Client);

    await screen.findByText("Luna Artista");
    await user.click(screen.getByLabelText("Select artist"));
    await user.click(await screen.findByRole("option", { name: "Luna Artista" }));
    await user.type(screen.getByLabelText(/date.*time/i), new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 16));
    await user.click(screen.getByRole("button", { name: /request appointment/i }));
    await screen.findByText(/secure your slot/i);
    await user.click(screen.getByText(/sort the deposit out later/i));
    expect(await screen.findByText("The studio will contact you about the deposit. The artist will confirm soon.")).toBeInTheDocument();
  });

  it("'Book another' resets the form", async () => {
    const user = userEvent.setup();
    renderForm();

    await screen.findByText("Luna Artista");
    await user.click(screen.getByLabelText("Select artist"));
    await user.click(await screen.findByRole("option", { name: "Luna Artista" }));
    await user.type(screen.getByLabelText(/date.*time/i), new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 16));
    await user.click(screen.getByRole("button", { name: /request appointment/i }));
    await screen.findByText("Appointment requested!");
    await user.click(screen.getByRole("button", { name: /book another/i }));
    expect(screen.getByRole("button", { name: /request appointment/i })).toBeInTheDocument();
  });
});

// ── MyBookingsSection ──────────────────────────────────────────────────────────

describe("MyBookingsSection", () => {
  it("shows empty-state text when there are no appointments", async () => {
    renderMyBookings();
    expect(await screen.findByText(/no upcoming bookings yet/i)).toBeInTheDocument();
  });

  it("shows loading spinner while fetching", () => {
    renderMyBookings();
    expect(screen.getByText(/loading your bookings/i)).toBeInTheDocument();
  });

  it("shows error message when the fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments/mine", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    renderMyBookings();
    expect(await screen.findByText(/couldn't load your bookings/i)).toBeInTheDocument();
  });

  it("renders an upcoming appointment", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments/mine", () =>
        HttpResponse.json([APPT_UPCOMING]),
      ),
    );
    renderMyBookings();
    await waitFor(() =>
      expect(screen.queryByText(/loading your bookings/i)).not.toBeInTheDocument(),
    );
    expect(screen.getByText("Luna Artista · 60 min")).toBeInTheDocument();
  });

  it("renders a past appointment under the 'Past' heading", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments/mine", () =>
        HttpResponse.json([APPT_PAST]),
      ),
    );
    renderMyBookings();
    expect(await screen.findByText("Past")).toBeInTheDocument();
    expect(screen.getByText("Luna Artista · 60 min")).toBeInTheDocument();
  });

  it("shows both upcoming and past sections when both exist", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments/mine", () =>
        HttpResponse.json([APPT_UPCOMING, APPT_PAST]),
      ),
    );
    renderMyBookings();
    expect(await screen.findByText("Past")).toBeInTheDocument();
    expect(screen.getAllByText("Luna Artista · 60 min")).toHaveLength(2);
  });

  it("shows 'Requested' status badge for a Pending appointment", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments/mine", () =>
        HttpResponse.json([APPT_UPCOMING]),
      ),
    );
    renderMyBookings();
    expect(await screen.findByText("Requested")).toBeInTheDocument();
  });

  it("shows 'Completed' status badge for a past completed appointment", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments/mine", () =>
        HttpResponse.json([APPT_PAST]),
      ),
    );
    renderMyBookings();
    expect(await screen.findByText("Completed")).toBeInTheDocument();
  });

  // ── Client self-cancel ─────────────────────────────────────────────────────

  it("shows a 'Cancel appointment' link for a Pending upcoming booking", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments/mine", () => HttpResponse.json([APPT_UPCOMING])),
    );
    renderMyBookings();
    expect(await screen.findByText("Cancel appointment")).toBeInTheDocument();
  });

  it("does not show 'Cancel appointment' for a past/completed booking", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments/mine", () => HttpResponse.json([APPT_PAST])),
    );
    renderMyBookings();
    await screen.findByText("Completed");
    expect(screen.queryByText("Cancel appointment")).not.toBeInTheDocument();
  });

  it("clicking 'Cancel appointment' shows a confirmation with a full-refund message when outside the notice window", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments/mine", () =>
        HttpResponse.json([{ ...APPT_UPCOMING, depositAmount: 50, depositStatus: "Paid" }]),
      ),
    );
    const user = userEvent.setup();
    renderMyBookings();
    await user.click(await screen.findByText("Cancel appointment"));
    expect(screen.getByText(/you'll receive a full refund/i)).toBeInTheDocument();
  });

  it("shows a forfeiture message when the appointment is within the notice window", async () => {
    const soon = new Date(Date.now() + 2 * 3_600_000).toISOString();
    server.use(
      http.get("http://localhost/api/v1/appointments/mine", () =>
        HttpResponse.json([{ ...APPT_UPCOMING, date: soon, depositAmount: 50, depositStatus: "Paid" }]),
      ),
    );
    const user = userEvent.setup();
    renderMyBookings();
    await user.click(await screen.findByText("Cancel appointment"));
    expect(screen.getByText(/cancelling now forfeits 100% of your deposit/i)).toBeInTheDocument();
  });

  it("'Keep booking' dismisses the confirmation without cancelling", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments/mine", () => HttpResponse.json([APPT_UPCOMING])),
    );
    const user = userEvent.setup();
    renderMyBookings();
    await user.click(await screen.findByText("Cancel appointment"));
    await user.click(screen.getByRole("button", { name: /keep booking/i }));
    expect(screen.getByText("Cancel appointment")).toBeInTheDocument();
  });

  it("confirming cancellation shows a success toast", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments/mine", () => HttpResponse.json([APPT_UPCOMING])),
    );
    const user = userEvent.setup();
    renderMyBookings();
    await user.click(await screen.findByText("Cancel appointment"));
    await user.click(screen.getByRole("button", { name: /yes, cancel/i }));
    expect(await screen.findByText("Appointment cancelled.")).toBeInTheDocument();
  });

  it("shows an error toast when cancellation fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments/mine", () => HttpResponse.json([APPT_UPCOMING])),
      http.delete("http://localhost/api/v1/appointments/:id", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderMyBookings();
    await user.click(await screen.findByText("Cancel appointment"));
    await user.click(screen.getByRole("button", { name: /yes, cancel/i }));
    expect(await screen.findByText("Failed to cancel appointment.")).toBeInTheDocument();
  });
});
