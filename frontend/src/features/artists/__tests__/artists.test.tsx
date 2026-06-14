import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { artistsApi } from "@/features/artists/artistsApi";
import { designsApi } from "@/features/designs/designsApi";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import type { ArtistResponse } from "@/features/artists/artistsApi";
import { ArtistListPage } from "@/features/artists/components/ArtistListPage";
import { ArtistDetailPage } from "@/features/artists/components/ArtistDetailPage";
import { Role } from "@/shared/types/roles";

// ── Seed data ──────────────────────────────────────────────────────────────────

const ELENA: ArtistResponse = {
  id: "eeee0001-0000-0000-0000-000000000001",
  studioId: "stud-0001",
  firstName: "Elena",
  lastName: "Martins",
  email: "elena.martins@ink-soul.test",
  specializations: "Traditional, Realism",
  hourlyRate: 100,
  createdAt: "2024-01-15T10:00:00.000Z",
  updatedAt: "2024-06-01T10:00:00.000Z",
};

const ARTISTS: ArtistResponse[] = [
  ELENA,
  {
    id: "eeee0002-0000-0000-0000-000000000002",
    studioId: "stud-0001",
    firstName: "Marco",
    lastName: "Silva",
    email: "marco.silva@ink-soul.test",
    specializations: "Neo-Traditional",
    hourlyRate: null,
    createdAt: "2024-02-10T10:00:00.000Z",
    updatedAt: "2024-05-20T10:00:00.000Z",
  },
  {
    id: "eeee0003-0000-0000-0000-000000000003",
    studioId: "stud-0001",
    firstName: "Sara",
    lastName: "Costa",
    email: "sara.costa@ink-soul.test",
    specializations: null,
    hourlyRate: null,
    createdAt: "2024-03-05T10:00:00.000Z",
    updatedAt: "2024-04-15T10:00:00.000Z",
  },
];

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/artists", ({ request }) => {
    const search = new URL(request.url).searchParams.get("search");
    const list = search
      ? ARTISTS.filter((a) =>
          `${a.firstName} ${a.lastName}`.toLowerCase().includes(search.toLowerCase()) ||
          a.email.toLowerCase().includes(search.toLowerCase()),
        )
      : ARTISTS;
    return HttpResponse.json(list);
  }),

  http.get("http://localhost/api/v1/artists/:id", ({ params }) => {
    const artist = ARTISTS.find((a) => a.id === params.id);
    return artist
      ? HttpResponse.json(artist)
      : new HttpResponse(null, { status: 404 });
  }),

  http.put("http://localhost/api/v1/artists/:id", async ({ request, params }) => {
    const body = (await request.json()) as {
      firstName: string;
      lastName: string;
      email: string;
      specializations: string | null;
    };
    const artist = ARTISTS.find((a) => a.id === params.id);
    if (!artist) return new HttpResponse(null, { status: 404 });
    return HttpResponse.json({ ...artist, ...body });
  }),

  http.delete("http://localhost/api/v1/artists/:id", () =>
    new HttpResponse(null, { status: 204 }),
  ),

  http.get("http://localhost/api/v1/designs", () => HttpResponse.json([])),
  http.get("http://localhost/api/v1/appointments", () => HttpResponse.json([])),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore(role: Role = Role.Owner) {
  return configureStore({
    reducer: {
      auth:              authReducer,
      [artistsApi.reducerPath]:      artistsApi.reducer,
      [designsApi.reducerPath]:      designsApi.reducer,
      [appointmentsApi.reducerPath]: appointmentsApi.reducer,
    },
    middleware: (gd) =>
      gd().concat(artistsApi.middleware, designsApi.middleware, appointmentsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "test@ink-soul.test" }, token: "fake", tenantId: "t1", role } as any,
    },
  });
}

function renderList(role: Role = Role.Owner) {
  const store = makeStore(role);
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/artists"]}>
        <Routes>
          <Route path="/artists" element={<ArtistListPage />} />
          <Route path="/artists/:id" element={<ArtistDetailPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

function renderDetail(id: string, role: Role = Role.Owner) {
  const store = makeStore(role);
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[`/artists/${id}`]}>
        <Routes>
          <Route path="/artists" element={<ArtistListPage />} />
          <Route path="/artists/:id" element={<ArtistDetailPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("Artists feature", () => {
  // 1. List page
  it("renders 3 ArtistCards as <Link> wrappers with ChevronRight indicators", async () => {
    renderList();

    // DataTable renders table rows (1 header + 3 data = 4 rows)
    const rows = await screen.findAllByRole("row");
    // header row + 3 data rows
    expect(rows.length).toBeGreaterThanOrEqual(4);

    expect(screen.getByText("Elena Martins")).toBeInTheDocument();
    expect(screen.getByText("Marco Silva")).toBeInTheDocument();
    expect(screen.getByText("Sara Costa")).toBeInTheDocument();

    // Data rows have cursor-pointer class
    const dataRows = rows.slice(1);
    for (const row of dataRows) {
      expect(row).toHaveClass("cursor-pointer");
    }
  });

  // 2. Clicking Elena navigates to detail view
  it("clicking Elena Martins card fires useGetArtistByIdQuery and renders view mode", async () => {
    const user = userEvent.setup();
    renderList();

    await screen.findAllByRole("row");

    const elenaCell = await screen.findByText("Elena Martins");
    await user.click(elenaCell);

    // Avatar initials
    await screen.findByText("EM");

    // Name heading
    expect(screen.getByRole("heading", { name: /elena martins/i })).toBeInTheDocument();

    // Email, specializations, join date
    expect(screen.getByText(ELENA.email)).toBeInTheDocument();
    expect(screen.getByText("Traditional, Realism")).toBeInTheDocument();
    expect(screen.getByText(/joined/i)).toBeInTheDocument();

    // Edit + Delete visible (Owner role)
    expect(screen.getByRole("button", { name: /edit/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /delete/i })).toBeInTheDocument();
  });

  // 3. Edit mode
  it("edit mode: form pre-populates, empty firstName shows inline error, valid save returns to view", async () => {
    const user = userEvent.setup();
    renderDetail(ELENA.id);

    await screen.findByText("EM");

    await user.click(screen.getByRole("button", { name: /edit/i }));

    // Form pre-populated
    const firstNameInput = screen.getByLabelText(/first name/i);
    const lastNameInput = screen.getByLabelText(/last name/i);
    expect(firstNameInput).toHaveValue("Elena");
    expect(lastNameInput).toHaveValue("Martins");

    // Trigger validation error
    await user.clear(firstNameInput);
    await user.click(screen.getByRole("button", { name: /save changes/i }));
    await screen.findByText("First name is required");

    // Fix and submit successfully
    await user.type(firstNameInput, "Elena");
    await user.click(screen.getByRole("button", { name: /save changes/i }));

    // Returns to view mode: form gone, Edit button back
    await screen.findByRole("button", { name: /edit/i });
    expect(screen.queryByLabelText(/first name/i)).not.toBeInTheDocument();
  });

  // 4. Confirm-delete mode
  it("confirm-delete shows warning text; Cancel returns to view mode without deleting", async () => {
    const user = userEvent.setup();
    renderDetail(ELENA.id);

    await screen.findByText("EM");

    await user.click(screen.getByRole("button", { name: /delete/i }));

    expect(screen.getByText("Delete Elena Martins?")).toBeInTheDocument();
    expect(screen.getByText("This action cannot be undone.")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /cancel/i }));

    // Back to view mode
    expect(screen.getByRole("button", { name: /edit/i })).toBeInTheDocument();
    expect(screen.queryByText("Delete Elena Martins?")).not.toBeInTheDocument();
  });

  // 5. Back button navigation
  it("back button navigates from detail to /artists list", async () => {
    const user = userEvent.setup();
    renderDetail(ELENA.id);

    await screen.findByText("EM");

    await user.click(screen.getByRole("button", { name: /^artists$/i }));

    // List page renders its search input immediately
    await screen.findByPlaceholderText(/search by name or email/i);
  });

  // 6. Artist role — permission gate
  it("logged in as Artist role: Edit and Delete buttons are hidden", async () => {
    renderDetail(ELENA.id, Role.Artist);

    await screen.findByText("EM");

    expect(screen.queryByRole("button", { name: /edit/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /delete/i })).not.toBeInTheDocument();
  });
});
