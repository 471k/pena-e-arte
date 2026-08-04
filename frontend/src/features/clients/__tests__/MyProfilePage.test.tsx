import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { clientsApi } from "@/features/clients/clientsApi";
import type { ClientResponse, ClientProfileResponse, TattooRecordResponse } from "@/features/clients/clientsApi";
import { MyProfilePage } from "@/features/clients/components/MyProfilePage";

// ── Seed data ──────────────────────────────────────────────────────────────────

const ME: ClientResponse = {
  id:        "cccc0001-0000-0000-0000-000000000001",
  studioId:  "stud-0001",
  firstName: "Ana",
  lastName:  "Ferreira",
  email:     "ana.ferreira@ink-soul.test",
  phone:     "+351 912 111 222",
  createdAt: "2024-01-10T09:00:00.000Z",
  userId:    "u1",
};

const PROFILE: ClientProfileResponse = {
  id:               "profile-001",
  clientId:         ME.id,
  studioId:         "stud-0001",
  dateOfBirth:      "1990-05-15",
  medicalNotes:     "None",
  allergies:        "Latex",
  bodyMapLocations: ["chest", "left_forearm"],
  updatedAt:        "2026-01-01T00:00:00.000Z",
  allowCrossTenantRead: false,
};

const TATTOOS: TattooRecordResponse[] = [
  {
    id:            "tattoo-001",
    clientId:      ME.id,
    artistId:      "artist-001",
    appointmentId: null,
    description:   "Rose on forearm",
    bodyLocation:  "left_forearm",
    photoUrls:     [],
    completedAt:   "2025-03-01T00:00:00.000Z",
    createdAt:     "2025-03-01T00:00:00.000Z",
  },
];

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/clients/me", () => HttpResponse.json(ME)),
  http.get("http://localhost/api/v1/clients/me/profile", () => HttpResponse.json(PROFILE)),
  http.get("http://localhost/api/v1/clients/me/tattoos", () => HttpResponse.json(TATTOOS)),
  http.patch("http://localhost/api/v1/clients/me/portable-profile", () =>
    new HttpResponse(null, { status: 204 }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      [clientsApi.reducerPath]: clientsApi.reducer,
    },
    middleware: (gd) => gd().concat(clientsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "ana.ferreira@ink-soul.test" }, token: "fake", tenantId: "t1", role: "client" } as any,
    },
  });
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <MyProfilePage />
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("MyProfilePage", () => {
  it("shows the client's name and email once loaded", async () => {
    renderPage();
    expect(await screen.findByText("Ana Ferreira")).toBeInTheDocument();
    expect(screen.getAllByText("ana.ferreira@ink-soul.test").length).toBeGreaterThanOrEqual(1);
  });

  it("shows an error message when the client fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients/me", () =>
        new HttpResponse(null, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText("Failed to load profile. Please try again.")).toBeInTheDocument();
  });

  it("Profile tab shows contact info and a read-only body map", async () => {
    renderPage();
    await screen.findByText("Ana Ferreira");
    expect(screen.getByText("+351 912 111 222")).toBeInTheDocument();
    expect(await screen.findByText("Body Map")).toBeInTheDocument();
    // Body map renders read-only (no role=button on zones)
    expect(screen.getByLabelText("Chest")).not.toHaveAttribute("role", "button");
  });

  it("Tattoo History tab shows the client's own tattoo records", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Ferreira");
    await user.click(screen.getByRole("tab", { name: /tattoo history/i }));
    expect(await screen.findByText("Rose on forearm")).toBeInTheDocument();
  });

  it("Tattoo History tab shows empty state when there are no records", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients/me/tattoos", () => HttpResponse.json([])),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Ferreira");
    await user.click(screen.getByRole("tab", { name: /tattoo history/i }));
    expect(await screen.findByText("No tattoo history recorded yet.")).toBeInTheDocument();
  });

  it("Sharing tab renders PortableProfileToggle reflecting allowCrossTenantRead=false", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Ferreira");
    await user.click(screen.getByRole("tab", { name: /sharing/i }));
    expect(await screen.findByRole("button", { name: "Off" })).toBeInTheDocument();
  });

  it("Sharing tab reflects allowCrossTenantRead=true", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients/me/profile", () =>
        HttpResponse.json({ ...PROFILE, allowCrossTenantRead: true }),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Ferreira");
    await user.click(screen.getByRole("tab", { name: /sharing/i }));
    expect(await screen.findByRole("button", { name: "On" })).toBeInTheDocument();
  });

  it("shows 'No profile information yet.' when the client has no profile (404)", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients/me/profile", () =>
        HttpResponse.json({ message: "Not found" }, { status: 404 }),
      ),
    );
    renderPage();
    await screen.findByText("Ana Ferreira");
    expect(await screen.findByText("No profile information yet.")).toBeInTheDocument();
  });

  it("Sharing tab shows fallback message when profile is missing", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients/me/profile", () =>
        HttpResponse.json({ message: "Not found" }, { status: 404 }),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Ana Ferreira");
    await user.click(screen.getByRole("tab", { name: /sharing/i }));
    expect(await screen.findByText(/profile sharing settings are unavailable/i)).toBeInTheDocument();
  });
});
