import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { clientsApi } from "@/features/clients/clientsApi";
import { artistsApi } from "@/features/artists/artistsApi";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { intakeFormsApi } from "@/features/forms/intakeFormsApi";
import { consentFormsApi } from "@/features/forms/consentFormsApi";
import type { ArtistResponse } from "@/features/artists/artistsApi";
import type {
  ClientResponse,
  ClientProfileResponse,
  TattooRecordResponse,
  PortableClientProfile,
} from "@/features/clients/clientsApi";
import type { AppointmentResponse } from "@/features/appointments/appointment.types";
import type { IntakeFormResponse, ConsentFormResponse } from "@/features/forms/form.types";
import { AppointmentStatus, DepositStatus } from "@/features/appointments/appointment.types";
import { ClientDetailPage } from "@/features/clients/components/ClientDetailPage";
import { Role } from "@/shared/types/roles";

// ── Seed data ──────────────────────────────────────────────────────────────────

const CLIENT_ID = "client-001";

const CLIENT: ClientResponse = {
  id:        CLIENT_ID,
  studioId:  "stud-0001",
  firstName: "Ana",
  lastName:  "Ferreira",
  email:     "ana.ferreira@ink-soul.test",
  phone:     "+351 912 111 222",
  createdAt: "2024-01-10T09:00:00.000Z",
  userId:    "u-ana",
};

const PROFILE: ClientProfileResponse = {
  id:               "profile-001",
  clientId:         CLIENT_ID,
  studioId:         "stud-0001",
  dateOfBirth:      "1990-05-15",
  medicalNotes:     "None",
  allergies:        "Latex",
  bodyMapLocations: ["chest"],
  updatedAt:        "2026-01-01T00:00:00.000Z",
  allowCrossTenantRead: false,
};

const ARTIST: ArtistResponse = {
  id:              "artist-001",
  studioId:        "stud-0001",
  firstName:       "Marta",
  lastName:        "Reis",
  email:           "marta@ink-soul.test",
  specializations: null,
  hourlyRate:      null,
  portfolioImages: [],
  slug: null,
  userId:          null,
  createdAt:       "2024-01-01T00:00:00.000Z",
  updatedAt:       "2024-01-01T00:00:00.000Z",
};

const TATTOO: TattooRecordResponse = {
  id:            "tattoo-001",
  clientId:      CLIENT_ID,
  artistId:      ARTIST.id,
  appointmentId: null,
  description:   "Rose on forearm",
  bodyLocation:  "left_forearm",
  photoUrls:     [],
  completedAt:   "2025-03-01T00:00:00.000Z",
  createdAt:     "2025-03-01T00:00:00.000Z",
};

const APPOINTMENT: AppointmentResponse = {
  id:              "appt-001",
  studioId:        "stud-0001",
  artistId:        ARTIST.id,
  clientId:        CLIENT_ID,
  date:            "2026-07-01T10:00:00.000Z",
  endDate:         "2026-07-01T12:00:00.000Z",
  durationMinutes: 120,
  status:          AppointmentStatus.Confirmed,
  depositStatus:   DepositStatus.Paid,
  depositAmount:   50,
  notes:           null,
  createdAt:       "2026-06-01T00:00:00.000Z",
};

const INTAKE_FORM: IntakeFormResponse = {
  id:            "intake-001",
  studioId:      "stud-0001",
  clientId:      CLIENT_ID,
  appointmentId: null,
  formData:      "{}",
  fileUrl:       null,
  submittedAt:   "2026-01-05T00:00:00.000Z",
  createdAt:     "2026-01-05T00:00:00.000Z",
};

const CONSENT_FORM: ConsentFormResponse = {
  id:            "consent-001",
  studioId:      "stud-0001",
  clientId:      CLIENT_ID,
  appointmentId: "appt-001",
  fileUrl:       null,
  signatureData: "sig",
  signedAt:      "2026-01-06T00:00:00.000Z",
  createdAt:     "2026-01-06T00:00:00.000Z",
};

const PORTABLE_PROFILE: PortableClientProfile = {
  displayName:      "Ana F.",
  bodyMapLocations: ["chest"],
  tattooHistory: [
    {
      bodyLocation:    "Chest",
      photoUrls:       [],
      description:     "Skull from another studio",
      completedAt:     "2024-11-01T00:00:00.000Z",
      artistFirstName: "Joana",
    },
  ],
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/clients/:id", () => HttpResponse.json(CLIENT)),
  http.get("http://localhost/api/v1/clients/:id/profile", () => HttpResponse.json(PROFILE)),
  http.put("http://localhost/api/v1/clients/:id/profile", async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    return HttpResponse.json({ ...PROFILE, ...body });
  }),
  http.patch("http://localhost/api/v1/clients/:id/profile/body-map", async ({ request }) => {
    const body = (await request.json()) as { locations: string[] };
    return HttpResponse.json({ ...PROFILE, bodyMapLocations: body.locations });
  }),
  http.get("http://localhost/api/v1/clients/:id/tattoos", () => HttpResponse.json([TATTOO])),
  http.get("http://localhost/api/v1/artists", () => HttpResponse.json([ARTIST])),
  http.get("http://localhost/api/v1/clients/:userId/portable-profile", () => HttpResponse.json(null)),
  http.get("http://localhost/api/v1/appointments", () => HttpResponse.json([APPOINTMENT])),
  http.get("http://localhost/api/v1/intake-forms", () => HttpResponse.json([INTAKE_FORM])),
  http.get("http://localhost/api/v1/consent-forms", () => HttpResponse.json([CONSENT_FORM])),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore(role: Role) {
  return configureStore({
    reducer: {
      auth: authReducer,
      [clientsApi.reducerPath]: clientsApi.reducer,
      [artistsApi.reducerPath]: artistsApi.reducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
      [intakeFormsApi.reducerPath]: intakeFormsApi.reducer,
      [consentFormsApi.reducerPath]: consentFormsApi.reducer,
    },
    middleware: (gd) =>
      gd().concat(
        clientsApi.middleware,
        artistsApi.middleware,
        appointmentsApi.middleware,
        intakeFormsApi.middleware,
        consentFormsApi.middleware,
      ),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "test@ink-soul.test" }, token: "fake", tenantId: "t1", role } as any,
    },
  });
}

function renderPage(role: Role = Role.Owner) {
  render(
    <Provider store={makeStore(role)}>
      <MemoryRouter initialEntries={[`/clients/${CLIENT_ID}`]}>
        <Routes>
          <Route path="/clients/:id" element={<ClientDetailPage />} />
          <Route path="/clients" element={<div data-testid="list-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("ClientDetailPage", () => {
  it("renders the client's name and contact info once loaded", async () => {
    renderPage();
    expect(await screen.findByText("Ana Ferreira")).toBeInTheDocument();
    expect(screen.getByText("ana.ferreira@ink-soul.test")).toBeInTheDocument();
    expect(screen.getByText("+351 912 111 222")).toBeInTheDocument();
  });

  it("shows 'Client not found.' when the client fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients/:id", () => new HttpResponse(null, { status: 500 })),
    );
    renderPage();
    expect(await screen.findByText("Client not found.")).toBeInTheDocument();
  });

  it("'Clients' back button (not-found state) navigates to /clients", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients/:id", () => new HttpResponse(null, { status: 500 })),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Client not found.");
    await user.click(screen.getByRole("button", { name: /back to clients/i }));
    expect(screen.getByTestId("list-page")).toBeInTheDocument();
  });

  it("'Clients' header button navigates to /clients", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Ferreira");
    await user.click(screen.getByRole("button", { name: /^clients$/i }));
    expect(screen.getByTestId("list-page")).toBeInTheDocument();
  });

  // ── Permissions ──────────────────────────────────────────────────────────────

  it("Artist role sees the 'Edit Profile' button", async () => {
    renderPage(Role.Artist);
    await screen.findByText("Ana Ferreira");
    expect(screen.getByRole("button", { name: /edit profile/i })).toBeInTheDocument();
  });

  it("Client role does NOT see the 'Edit Profile' button", async () => {
    renderPage(Role.Client);
    await screen.findByText("Ana Ferreira");
    expect(screen.queryByRole("button", { name: /edit profile/i })).not.toBeInTheDocument();
  });

  it("shows 'Add Profile' instead of 'Edit Profile' when there is no profile yet (404)", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients/:id/profile", () =>
        HttpResponse.json({ message: "Not found" }, { status: 404 }),
      ),
    );
    renderPage(Role.Artist);
    await screen.findByText("Ana Ferreira");
    expect(screen.getByRole("button", { name: /add profile/i })).toBeInTheDocument();
  });

  // ── Tabs: Profile ────────────────────────────────────────────────────────────

  it("Profile tab shows health details and a read-only body map", async () => {
    renderPage();
    await screen.findByText("Ana Ferreira");
    expect(screen.getByText("Latex")).toBeInTheDocument();
    expect(screen.getByText("None")).toBeInTheDocument();
    expect(await screen.findByText("Body Map")).toBeInTheDocument();
    expect(screen.getByLabelText("Chest")).not.toHaveAttribute("role", "button");
  });

  it("Profile tab shows 'No profile information yet.' on 404", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients/:id/profile", () =>
        HttpResponse.json({ message: "Not found" }, { status: 404 }),
      ),
    );
    renderPage();
    await screen.findByText("Ana Ferreira");
    expect(await screen.findByText("No profile information yet.")).toBeInTheDocument();
  });

  it("Artist role can edit the body map and save changes", async () => {
    const user = userEvent.setup();
    renderPage(Role.Artist);
    await screen.findByText("Ana Ferreira");
    await screen.findByText("Body Map");
    await user.click(screen.getByTestId("edit-body-map"));
    expect(screen.getByLabelText("Chest")).toHaveAttribute("role", "button");
    await user.click(screen.getByLabelText("Neck"));
    await user.click(screen.getByRole("button", { name: /save/i }));
    expect(await screen.findByTestId("edit-body-map")).toBeInTheDocument();
  });

  it("'Edit Profile' opens the profile edit form pre-filled with existing values", async () => {
    const user = userEvent.setup();
    renderPage(Role.Artist);
    await screen.findByText("Ana Ferreira");
    await user.click(screen.getByRole("button", { name: /edit profile/i }));
    expect(screen.getByLabelText(/allergies/i)).toHaveValue("Latex");
    expect(screen.getByLabelText(/medical notes/i)).toHaveValue("None");
  });

  it("saving the profile form returns to the tabs view", async () => {
    const user = userEvent.setup();
    renderPage(Role.Artist);
    await screen.findByText("Ana Ferreira");
    await user.click(screen.getByRole("button", { name: /edit profile/i }));
    await user.click(screen.getByRole("button", { name: /save profile/i }));
    expect(await screen.findByRole("tab", { name: /profile/i })).toBeInTheDocument();
  });

  it("'Cancel' while editing the profile returns to the tabs view without saving", async () => {
    const user = userEvent.setup();
    renderPage(Role.Artist);
    await screen.findByText("Ana Ferreira");
    await user.click(screen.getByRole("button", { name: /edit profile/i }));
    await user.click(screen.getByRole("button", { name: /cancel/i }));
    expect(screen.getByRole("tab", { name: /profile/i })).toBeInTheDocument();
  });

  // ── Tabs: Tattoo History ─────────────────────────────────────────────────────

  it("Tattoo History tab renders the TattooHistorySection for this client", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Ferreira");
    await user.click(screen.getByRole("tab", { name: /tattoo history/i }));
    expect(await screen.findByText("Rose on forearm")).toBeInTheDocument();
  });

  // ── Tabs: Cross-Studio ───────────────────────────────────────────────────────

  it("does not show the Cross-Studio tab when there is no portable profile", async () => {
    renderPage();
    await screen.findByText("Ana Ferreira");
    expect(screen.queryByRole("tab", { name: /cross-studio/i })).not.toBeInTheDocument();
  });

  it("shows the Cross-Studio tab with shared tattoo history when a portable profile exists", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients/:userId/portable-profile", () =>
        HttpResponse.json(PORTABLE_PROFILE),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Ferreira");
    const tab = await screen.findByRole("tab", { name: /cross-studio/i });
    await user.click(tab);
    expect(await screen.findByText(/skull from another studio/i)).toBeInTheDocument();
    expect(screen.getByText(/joana/i)).toBeInTheDocument();
  });

  it("Cross-Studio tab shows empty state when tattooHistory is empty", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients/:userId/portable-profile", () =>
        HttpResponse.json({ ...PORTABLE_PROFILE, tattooHistory: [] }),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Ferreira");
    const tab = await screen.findByRole("tab", { name: /cross-studio/i });
    await user.click(tab);
    expect(await screen.findByText("No cross-studio history available.")).toBeInTheDocument();
  });

  // ── Tabs: Forms ──────────────────────────────────────────────────────────────

  it("Forms tab lists intake and consent forms with links to their detail pages", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Ferreira");
    await user.click(screen.getByRole("tab", { name: /^forms$/i }));
    await screen.findByText("Intake Forms");
    const intakeLink = screen.getByRole("link", { name: /5 jan(uary)? 2026/i });
    expect(intakeLink).toHaveAttribute("href", `/forms/intake/${INTAKE_FORM.id}`);
    const consentLink = screen.getByRole("link", { name: /6 jan(uary)? 2026/i });
    expect(consentLink).toHaveAttribute("href", `/forms/consent/${CONSENT_FORM.id}`);
  });

  it("Forms tab shows empty states when there are no forms", async () => {
    server.use(
      http.get("http://localhost/api/v1/intake-forms", () => HttpResponse.json([])),
      http.get("http://localhost/api/v1/consent-forms", () => HttpResponse.json([])),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Ferreira");
    await user.click(screen.getByRole("tab", { name: /^forms$/i }));
    expect(await screen.findByText("No intake forms.")).toBeInTheDocument();
    expect(screen.getByText("No consent forms.")).toBeInTheDocument();
  });

  // ── Tabs: Appointments ───────────────────────────────────────────────────────

  it("Appointments tab lists this client's appointments with status badges", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Ferreira");
    await user.click(screen.getByRole("tab", { name: /appointments/i }));
    const link = await screen.findByRole("link", { name: /confirmed/i });
    expect(link).toHaveAttribute("href", `/appointments/${APPOINTMENT.id}`);
    expect(within(link).getByText("Confirmed")).toBeInTheDocument();
    expect(within(link).getByText("120 min")).toBeInTheDocument();
  });

  it("Appointments tab filters out appointments belonging to other clients", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () =>
        HttpResponse.json([APPOINTMENT, { ...APPOINTMENT, id: "appt-other", clientId: "someone-else" }]),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Ferreira");
    await user.click(screen.getByRole("tab", { name: /appointments/i }));
    expect(await screen.findAllByRole("link")).toHaveLength(1);
  });

  it("Appointments tab shows empty state when there are none", async () => {
    server.use(
      http.get("http://localhost/api/v1/appointments", () => HttpResponse.json([])),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Ferreira");
    await user.click(screen.getByRole("tab", { name: /appointments/i }));
    expect(await screen.findByText("No appointments found.")).toBeInTheDocument();
  });
});
