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
import type { ArtistResponse } from "@/features/artists/artistsApi";
import type { TattooRecordResponse } from "@/features/clients/clientsApi";
import { TattooRecordDetailPage } from "@/features/clients/components/TattooRecordDetailPage";
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
  photoUrls:     ["https://files.test/photo1.jpg"],
  completedAt:   "2025-03-01T00:00:00.000Z",
  createdAt:     "2025-03-01T00:00:00.000Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

let currentRecord: TattooRecordResponse = RECORD;

const server = setupServer(
  http.get("http://localhost/api/v1/clients/:clientId/tattoos/:tattooId", () => HttpResponse.json(currentRecord)),
  http.get("http://localhost/api/v1/artists", () => HttpResponse.json([ARTIST])),
  http.patch("http://localhost/api/v1/clients/:clientId/tattoos/:tattooId", async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    currentRecord = { ...currentRecord, ...body };
    return HttpResponse.json(currentRecord);
  }),
  http.delete("http://localhost/api/v1/clients/:clientId/tattoos/:tattooId", () =>
    new HttpResponse(null, { status: 204 }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); currentRecord = RECORD; });
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

function renderPage(role: Role = Role.Owner) {
  render(
    <Provider store={makeStore(role)}>
      <MemoryRouter initialEntries={[`/clients/${CLIENT_ID}/tattoos/${RECORD.id}`]}>
        <Routes>
          <Route path="/clients/:id/tattoos/:tattooId" element={<TattooRecordDetailPage />} />
          <Route path="/clients/:id" element={<div data-testid="client-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("TattooRecordDetailPage", () => {
  it("renders record details once loaded", async () => {
    renderPage();
    expect(await screen.findByText("Rose on forearm")).toBeInTheDocument();
    expect(screen.getByText("Left Forearm")).toBeInTheDocument();
    expect(screen.getByText("Marta Reis")).toBeInTheDocument();
  });

  it("shows the photo list when photoUrls is non-empty", async () => {
    renderPage();
    await screen.findByText("Rose on forearm");
    expect(screen.getByText("1 photo")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Photo 1" })).toHaveAttribute(
      "href",
      "https://files.test/photo1.jpg",
    );
  });

  it("shows 'Tattoo record not found.' on fetch error", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients/:clientId/tattoos/:tattooId", () =>
        new HttpResponse(null, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText("Tattoo record not found.")).toBeInTheDocument();
  });

  // ── Permissions ──────────────────────────────────────────────────────────────

  it("Artist role sees Edit and Delete buttons", async () => {
    renderPage(Role.Artist);
    await screen.findByText("Rose on forearm");
    expect(screen.getByRole("button", { name: /edit/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /delete/i })).toBeInTheDocument();
  });

  it("Client role does NOT see Edit or Delete buttons", async () => {
    renderPage(Role.Client);
    await screen.findByText("Rose on forearm");
    expect(screen.queryByRole("button", { name: /edit/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /delete/i })).not.toBeInTheDocument();
  });

  // ── Navigation ───────────────────────────────────────────────────────────────

  it("'Client' back button navigates to the client detail page", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Rose on forearm");
    await user.click(screen.getByRole("button", { name: /client/i }));
    expect(screen.getByTestId("client-page")).toBeInTheDocument();
  });

  // ── Edit flow ────────────────────────────────────────────────────────────────

  it("clicking 'Edit' opens the edit form pre-filled with current values", async () => {
    const user = userEvent.setup();
    renderPage(Role.Artist);
    await screen.findByText("Rose on forearm");
    await user.click(screen.getByRole("button", { name: /edit/i }));
    expect(screen.getByText("Edit Record")).toBeInTheDocument();
    expect(screen.getByLabelText("Description")).toHaveValue("Rose on forearm");
    expect(screen.getByLabelText("Body location")).toHaveValue("left_forearm");
    expect(screen.getByLabelText("Date completed")).toHaveValue("2025-03-01");
  });

  it("'Cancel' in edit mode returns to view mode", async () => {
    const user = userEvent.setup();
    renderPage(Role.Artist);
    await screen.findByText("Rose on forearm");
    await user.click(screen.getByRole("button", { name: /edit/i }));
    await user.click(screen.getByRole("button", { name: /cancel/i }));
    expect(screen.queryByText("Edit Record")).not.toBeInTheDocument();
    expect(screen.getByText("Rose on forearm")).toBeInTheDocument();
  });

  it("clearing the description and saving shows a validation error", async () => {
    const user = userEvent.setup();
    renderPage(Role.Artist);
    await screen.findByText("Rose on forearm");
    await user.click(screen.getByRole("button", { name: /edit/i }));
    await user.clear(screen.getByLabelText("Description"));
    await user.click(screen.getByRole("button", { name: /save changes/i }));
    expect(await screen.findByText("Required")).toBeInTheDocument();
  });

  it("saving valid changes returns to view mode with updated values", async () => {
    const user = userEvent.setup();
    renderPage(Role.Artist);
    await screen.findByText("Rose on forearm");
    await user.click(screen.getByRole("button", { name: /edit/i }));
    await user.clear(screen.getByLabelText("Description"));
    await user.type(screen.getByLabelText("Description"), "Updated rose design");
    await user.click(screen.getByRole("button", { name: /save changes/i }));
    expect(await screen.findByText("Updated rose design")).toBeInTheDocument();
    expect(screen.queryByText("Edit Record")).not.toBeInTheDocument();
  });

  // ── Delete flow ──────────────────────────────────────────────────────────────

  it("clicking 'Delete' opens a confirmation card", async () => {
    const user = userEvent.setup();
    renderPage(Role.Artist);
    await screen.findByText("Rose on forearm");
    await user.click(screen.getByRole("button", { name: /delete/i }));
    expect(screen.getByText("Delete this tattoo record?")).toBeInTheDocument();
  });

  it("'Cancel' in the confirmation card keeps the record and returns to view mode", async () => {
    const user = userEvent.setup();
    renderPage(Role.Artist);
    await screen.findByText("Rose on forearm");
    await user.click(screen.getByRole("button", { name: /delete/i }));
    const card = screen.getByText("Delete this tattoo record?").closest("div")!;
    await user.click(within(card).getByRole("button", { name: /cancel/i }));
    expect(screen.queryByText("Delete this tattoo record?")).not.toBeInTheDocument();
  });

  it("confirming delete navigates back to the client detail page", async () => {
    const user = userEvent.setup();
    renderPage(Role.Artist);
    await screen.findByText("Rose on forearm");
    await user.click(screen.getByRole("button", { name: /delete/i }));
    await user.click(screen.getByRole("button", { name: "Delete" }));
    expect(await screen.findByTestId("client-page")).toBeInTheDocument();
  });
});
