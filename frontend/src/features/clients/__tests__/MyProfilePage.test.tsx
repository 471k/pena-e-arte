import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
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
  id:         "cccc0001-0000-0000-0000-000000000001",
  studioId:   "stud-0001",
  firstName:  "Ana",
  lastName:   "Ferreira",
  email:      "ana.ferreira@ink-soul.test",
  phone:      "+351 912 111 222",
  createdAt:  "2024-01-10T09:00:00.000Z",
  userId:     "u1",
  artistId:   null,
  artistName: null,
  erasureRequestedAt: null, archivedAt: null,
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

interface CapturedPatch { body: unknown | null }

/** Registers a PATCH /clients/me handler that records the JSON body and echoes a merged client. */
function capturePatch(status = 200): CapturedPatch {
  const captured: CapturedPatch = { body: null };
  server.use(
    http.patch("http://localhost/api/v1/clients/me", async ({ request }) => {
      captured.body = await request.json();
      if (status !== 200) return HttpResponse.json({ message: "nope" }, { status });
      return HttpResponse.json({ ...ME, ...(captured.body as object) });
    }),
  );
  return captured;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("MyProfilePage", () => {
  it("shows the client's name and email once loaded", async () => {
    renderPage();
    expect(await screen.findByText("Ana Ferreira")).toBeInTheDocument();
    expect(screen.getAllByText("ana.ferreira@ink-soul.test").length).toBeGreaterThanOrEqual(1);
  });

  it("shows a generic error with a retry option when the client fetch fails (non-404)", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients/me", () =>
        new HttpResponse(null, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText("Failed to load profile.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /try again/i })).toBeInTheDocument();
  });

  // A studio-less client (see RegisterUserHandler) has no per-studio Client row until
  // they book at a studio for the first time — GetMyClientQuery 404s by design, not a
  // transient failure. Must show an actionable empty state, not a scary "failed" error.
  it("shows a 'join a studio' empty state, not a generic error, when the client has no Client row yet (404)", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients/me", () =>
        HttpResponse.json({ message: "Client not found" }, { status: 404 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/haven't joined a studio yet/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /browse studios/i })).toHaveAttribute("href", "/discover");
    expect(screen.queryByText("Failed to load profile.")).not.toBeInTheDocument();
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

  describe("editing contact details", () => {
    it("shows an Edit button on the Contact card that opens a form prefilled with the current values", async () => {
      const user = userEvent.setup();
      renderPage();
      await screen.findByText("Ana Ferreira");

      await user.click(screen.getByRole("button", { name: "Edit contact details" }));

      expect(screen.getByLabelText("First name")).toHaveValue("Ana");
      expect(screen.getByLabelText("Last name")).toHaveValue("Ferreira");
      // Email is the sign-in identity: shown, never an input, and points at the change-email flow.
      expect(screen.queryByRole("textbox", { name: /email/i })).not.toBeInTheDocument();
      expect(screen.getByRole("link", { name: "Change email" })).toHaveAttribute("href", "/account/change-email");
    });

    it("saves name changes and normalises the existing spaced phone to strict E.164", async () => {
      const captured = capturePatch();
      const user = userEvent.setup();
      renderPage();
      await screen.findByText("Ana Ferreira");

      await user.click(screen.getByRole("button", { name: "Edit contact details" }));
      const first = screen.getByLabelText("First name");
      await user.clear(first);
      await user.type(first, "Anabela");
      await user.click(screen.getByRole("button", { name: "Save" }));

      await waitFor(() => expect(captured.body).not.toBeNull());
      // ME.phone is "+351 912 111 222" (legacy spaced form) — the API only accepts "+351912111222".
      expect(captured.body).toEqual({ firstName: "Anabela", lastName: "Ferreira", phone: "+351912111222" });
      // Form closes on success.
      await waitFor(() => expect(screen.queryByLabelText("First name")).not.toBeInTheDocument());
    });

    it("sends phone: null when the number is cleared, so a client can remove their number", async () => {
      const captured = capturePatch();
      server.use(
        http.get("http://localhost/api/v1/clients/me", () => HttpResponse.json({ ...ME, phone: null })),
      );
      const user = userEvent.setup();
      renderPage();
      await screen.findByText("Ana Ferreira");

      await user.click(screen.getByRole("button", { name: "Edit contact details" }));
      await user.click(screen.getByRole("button", { name: "Save" }));

      await waitFor(() => expect(captured.body).not.toBeNull());
      expect(captured.body).toEqual({ firstName: "Ana", lastName: "Ferreira", phone: null });
    });

    it("blocks the save and shows field errors when a required name is cleared", async () => {
      const captured = capturePatch();
      const user = userEvent.setup();
      renderPage();
      await screen.findByText("Ana Ferreira");

      await user.click(screen.getByRole("button", { name: "Edit contact details" }));
      await user.clear(screen.getByLabelText("First name"));
      await user.click(screen.getByRole("button", { name: "Save" }));

      expect(await screen.findByText("First name is required")).toBeInTheDocument();
      expect(captured.body).toBeNull();
    });

    it("keeps the form open with the user's edits when the server rejects the save", async () => {
      capturePatch(400);
      const user = userEvent.setup();
      renderPage();
      await screen.findByText("Ana Ferreira");

      await user.click(screen.getByRole("button", { name: "Edit contact details" }));
      const first = screen.getByLabelText("First name");
      await user.clear(first);
      await user.type(first, "Anabela");
      await user.click(screen.getByRole("button", { name: "Save" }));

      await waitFor(() => expect(screen.getByRole("button", { name: "Save" })).not.toBeDisabled());
      expect(screen.getByLabelText("First name")).toHaveValue("Anabela");
    });

    it("Cancel discards edits and leaves the saved details untouched", async () => {
      const captured = capturePatch();
      const user = userEvent.setup();
      renderPage();
      await screen.findByText("Ana Ferreira");

      await user.click(screen.getByRole("button", { name: "Edit contact details" }));
      await user.type(screen.getByLabelText("First name"), "zzz");
      await user.click(screen.getByRole("button", { name: "Cancel" }));

      expect(screen.queryByLabelText("First name")).not.toBeInTheDocument();
      expect(captured.body).toBeNull();
      // Re-opening re-seeds from the server value, not the abandoned draft.
      await user.click(screen.getByRole("button", { name: "Edit contact details" }));
      expect(screen.getByLabelText("First name")).toHaveValue("Ana");
    });
  });
});
