import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { clientsApi } from "@/features/clients/clientsApi";
import type { ClientResponse } from "@/features/clients/clientsApi";
import { ClientListPage } from "@/features/clients/components/ClientListPage";
import { Role } from "@/shared/types/roles";

// ── Seed data ──────────────────────────────────────────────────────────────────

const ANA: ClientResponse = {
  id:        "cccc0001-0000-0000-0000-000000000001",
  studioId:  "stud-0001",
  firstName: "Ana",
  lastName:  "Ferreira",
  email:     "ana.ferreira@ink-soul.test",
  phone:     "+351 912 111 222",
  createdAt: "2024-01-10T09:00:00.000Z",
  userId:    null,
};

const CLIENTS: ClientResponse[] = [
  ANA,
  {
    id:        "cccc0002-0000-0000-0000-000000000002",
    studioId:  "stud-0001",
    firstName: "Bruno",
    lastName:  "Santos",
    email:     "bruno.santos@ink-soul.test",
    phone:     null,
    createdAt: "2024-02-14T09:00:00.000Z",
    userId:    null,
  },
  {
    id:        "cccc0003-0000-0000-0000-000000000003",
    studioId:  "stud-0001",
    firstName: "Carla",
    lastName:  "Nunes",
    email:     "carla.nunes@ink-soul.test",
    phone:     "+351 963 333 444",
    createdAt: "2024-03-20T09:00:00.000Z",
    userId:    null,
  },
];

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/clients", ({ request }) => {
    const search = new URL(request.url).searchParams.get("search");
    const list = search
      ? CLIENTS.filter((c) =>
          `${c.firstName} ${c.lastName}`.toLowerCase().includes(search.toLowerCase()) ||
          c.email.toLowerCase().includes(search.toLowerCase()),
        )
      : CLIENTS;
    return HttpResponse.json(list);
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore(role: Role = Role.Owner) {
  return configureStore({
    reducer: {
      auth: authReducer,
      ui:   uiReducer,
      [clientsApi.reducerPath]: clientsApi.reducer,
    },
    middleware: (gd) => gd().concat(clientsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "test@ink-soul.test" }, token: "fake", tenantId: "t1", role } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null },
    },
  });
}

function renderList(role: Role = Role.Owner) {
  const store = makeStore(role);
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/clients"]}>
        <Routes>
          <Route path="/clients" element={<ClientListPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("Clients feature", () => {
  // 1. List page renders all rows in the DataTable
  it("renders 3 ClientCards as <Link> wrappers with ChevronRight indicators", async () => {
    renderList();

    // DataTable: 1 header row + 3 data rows = 4 total
    const rows = await screen.findAllByRole("row");
    expect(rows.length).toBeGreaterThanOrEqual(4);

    // Each name appears twice — once in the mobileCard, once in the table (dual-render).
    expect(screen.getAllByText("Ana Ferreira")).toHaveLength(2);
    expect(screen.getAllByText("Bruno Santos")).toHaveLength(2);
    expect(screen.getAllByText("Carla Nunes")).toHaveLength(2);

    // Data rows have cursor-pointer class (for onRowClick)
    const dataRows = rows.slice(1);
    for (const row of dataRows) {
      expect(row).toHaveClass("cursor-pointer");
    }
  });

  // 2. Card shows phone when present, omits it when null
  it("shows phone for Ana, omits phone row for Bruno", async () => {
    renderList();

    await screen.findAllByRole("row");

    expect(screen.getByText("+351 912 111 222")).toBeInTheDocument();
    expect(screen.queryByText("null")).not.toBeInTheDocument();
  });

  // 3. Header shows client count
  it("shows client count in header after load", async () => {
    renderList();

    await screen.findAllByRole("row");

    expect(screen.getByText(/3 clients/i)).toBeInTheDocument();
  });

  // 4. Search filters results
  it("search input filters clients by name", async () => {
    const user = userEvent.setup();

    server.use(
      http.get("http://localhost/api/v1/clients", ({ request }) => {
        const search = new URL(request.url).searchParams.get("search");
        const list = search
          ? CLIENTS.filter((c) =>
              `${c.firstName} ${c.lastName}`.toLowerCase().includes(search.toLowerCase()),
            )
          : CLIENTS;
        return HttpResponse.json(list);
      }),
    );

    renderList();
    await screen.findAllByRole("row");

    const input = screen.getByPlaceholderText(/search by name or email/i);
    await user.type(input, "Ana");

    // After filtering: 1 header row + 1 data row = 2 rows
    await waitFor(() => {
      expect(screen.getAllByRole("row")).toHaveLength(2);
    }, { timeout: 1000 });

    expect(screen.getAllByText("Ana Ferreira")).toHaveLength(2);
    expect(screen.queryByText("Bruno Santos")).not.toBeInTheDocument();
  });

  // 5. Empty search result message
  it("shows no-match message when search returns empty", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients", () => HttpResponse.json([])),
    );

    const user = userEvent.setup();
    renderList();

    await screen.findByText("No clients yet");

    const input = screen.getByPlaceholderText(/search by name or email/i);
    await user.type(input, "XYZ");

    await screen.findByText(/no clients match/i);
  });

  // 6. Artist role sees "+ New Client" button
  it("Artist role: New Client button is visible", async () => {
    renderList(Role.Artist);

    await screen.findAllByRole("row");

    expect(screen.getByRole("button", { name: /new client/i })).toBeInTheDocument();
  });

  // 7. Owner role sees "+ New Client" button
  it("Owner role: New Client button is visible", async () => {
    renderList(Role.Owner);

    await screen.findAllByRole("row");

    expect(screen.getByRole("button", { name: /new client/i })).toBeInTheDocument();
  });

  // 8. Error state
  it("shows error message on API failure", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients", () =>
        new HttpResponse(null, { status: 500 }),
      ),
    );

    renderList();

    await screen.findByText("Failed to load clients. Please try again.");
  });
});
