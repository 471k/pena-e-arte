import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { Toaster } from "sonner";

import authReducer from "@/features/auth/authSlice";
import { clientsApi } from "@/features/clients/clientsApi";
import { artistsApi } from "@/features/artists/artistsApi";
import type { ArtistResponse } from "@/features/artists/artistsApi";
import type { TattooRecordResponse } from "@/features/clients/clientsApi";
import { TattooHistorySection } from "@/features/clients/components/TattooHistorySection";
import { Role } from "@/shared/types/roles";

// ── Seed data ──────────────────────────────────────────────────────────────────

const CLIENT_ID = "client-001";

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

const RECORD: TattooRecordResponse = {
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

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/clients/:clientId/tattoos", () => HttpResponse.json([RECORD])),
  http.get("http://localhost/api/v1/artists", () => HttpResponse.json([ARTIST])),
  http.post("http://localhost/api/v1/clients/:clientId/tattoos", async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    return HttpResponse.json({
      id: "tattoo-002", clientId: CLIENT_ID, appointmentId: null,
      createdAt: "2026-06-15T00:00:00.000Z",
      ...body,
    });
  }),
  http.delete("http://localhost/api/v1/clients/:clientId/tattoos/:id", () =>
    new HttpResponse(null, { status: 204 }),
  ),
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
    },
    middleware: (gd) => gd().concat(clientsApi.middleware, artistsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "test@ink-soul.test" }, token: "fake", tenantId: "t1", role } as any,
    },
  });
}

function renderSection(role: Role = Role.Owner) {
  render(
    <Provider store={makeStore(role)}>
      <Toaster />
      <MemoryRouter>
        <TattooHistorySection clientId={CLIENT_ID} />
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("TattooHistorySection", () => {
  it("renders existing tattoo records", async () => {
    renderSection();
    expect(await screen.findByText("Rose on forearm")).toBeInTheDocument();
    expect(screen.getByText("Left Forearm")).toBeInTheDocument();
    expect(screen.getByText("Marta Reis")).toBeInTheDocument();
  });

  it("shows the record count next to the header", async () => {
    renderSection();
    await screen.findByText("Rose on forearm");
    expect(screen.getByText("(1)")).toBeInTheDocument();
  });

  it("shows empty state when there are no records", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients/:clientId/tattoos", () => HttpResponse.json([])),
    );
    renderSection();
    expect(await screen.findByText("No tattoo records yet.")).toBeInTheDocument();
  });

  it("shows error state when the fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients/:clientId/tattoos", () =>
        new HttpResponse(null, { status: 500 }),
      ),
    );
    renderSection();
    expect(await screen.findByText("Failed to load tattoo records.")).toBeInTheDocument();
  });

  // ── Permissions ──────────────────────────────────────────────────────────────

  it("Artist role sees the 'Add' button", async () => {
    renderSection(Role.Artist);
    await screen.findByText("Rose on forearm");
    expect(screen.getByTestId("add-tattoo-record")).toBeInTheDocument();
  });

  it("Owner role sees the 'Add' button", async () => {
    renderSection(Role.Owner);
    await screen.findByText("Rose on forearm");
    expect(screen.getByTestId("add-tattoo-record")).toBeInTheDocument();
  });

  it("Client role does NOT see the 'Add' button", async () => {
    renderSection(Role.Client);
    await screen.findByText("Rose on forearm");
    expect(screen.queryByTestId("add-tattoo-record")).not.toBeInTheDocument();
  });

  it("Owner role sees the delete affordance on a record", async () => {
    renderSection(Role.Owner);
    await screen.findByText("Rose on forearm");
    expect(screen.getByLabelText("Delete tattoo record")).toBeInTheDocument();
  });

  it("Artist role does NOT see the delete affordance on a record", async () => {
    renderSection(Role.Artist);
    await screen.findByText("Rose on forearm");
    expect(screen.queryByLabelText("Delete tattoo record")).not.toBeInTheDocument();
  });

  // ── Add flow ─────────────────────────────────────────────────────────────────

  it("clicking 'Add' opens the new-record form", async () => {
    const user = userEvent.setup();
    renderSection(Role.Artist);
    await screen.findByText("Rose on forearm");
    await user.click(screen.getByTestId("add-tattoo-record"));
    expect(screen.getByText("New Record")).toBeInTheDocument();
  });

  it("'Cancel' closes the new-record form", async () => {
    const user = userEvent.setup();
    renderSection(Role.Artist);
    await screen.findByText("Rose on forearm");
    await user.click(screen.getByTestId("add-tattoo-record"));
    await user.click(screen.getByRole("button", { name: /cancel/i }));
    expect(screen.queryByText("New Record")).not.toBeInTheDocument();
  });

  it("submitting the add form without required fields shows validation errors", async () => {
    const user = userEvent.setup();
    renderSection(Role.Artist);
    await screen.findByText("Rose on forearm");
    await user.click(screen.getByTestId("add-tattoo-record"));
    await user.click(screen.getByRole("button", { name: /save record/i }));
    expect((await screen.findAllByText("Required")).length).toBeGreaterThan(0);
  });

  // ── Delete flow ──────────────────────────────────────────────────────────────

  it("clicking the delete icon opens a confirmation dialog", async () => {
    const user = userEvent.setup();
    renderSection(Role.Owner);
    await screen.findByText("Rose on forearm");
    await user.click(screen.getByLabelText("Delete tattoo record"));
    expect(screen.getByText("Delete tattoo record?")).toBeInTheDocument();
  });

  it("'Cancel' in the confirmation dialog keeps the record", async () => {
    const user = userEvent.setup();
    renderSection(Role.Owner);
    await screen.findByText("Rose on forearm");
    await user.click(screen.getByLabelText("Delete tattoo record"));
    await user.click(screen.getByRole("button", { name: /cancel/i }));
    expect(screen.queryByText("Delete tattoo record?")).not.toBeInTheDocument();
    expect(screen.getByText("Rose on forearm")).toBeInTheDocument();
  });

  it("confirming delete shows a success toast", async () => {
    const user = userEvent.setup();
    renderSection(Role.Owner);
    await screen.findByText("Rose on forearm");
    await user.click(screen.getByLabelText("Delete tattoo record"));
    await user.click(screen.getByRole("button", { name: "Delete" }));
    expect(await screen.findByText("Tattoo record deleted.")).toBeInTheDocument();
  });

  it("delete failure shows an error toast", async () => {
    server.use(
      http.delete("http://localhost/api/v1/clients/:clientId/tattoos/:id", () =>
        HttpResponse.json({ message: "Cannot delete" }, { status: 422 }),
      ),
    );
    const user = userEvent.setup();
    renderSection(Role.Owner);
    await screen.findByText("Rose on forearm");
    await user.click(screen.getByLabelText("Delete tattoo record"));
    await user.click(screen.getByRole("button", { name: "Delete" }));
    expect(await screen.findByText("Failed to delete tattoo record.")).toBeInTheDocument();
  });

  // ── Navigation ───────────────────────────────────────────────────────────────

  it("each record card links to its detail route", async () => {
    renderSection();
    await screen.findByText("Rose on forearm");
    const link = screen.getByText("Rose on forearm").closest("a");
    expect(link).toHaveAttribute("href", `/clients/${CLIENT_ID}/tattoos/${RECORD.id}`);
  });
});
