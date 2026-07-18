import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
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

// ── Seed data ──────────────────────────────────────────────────────────────────

const CLIENT_A: ClientResponse = {
  id:        "client-0001",
  studioId:  "stud-0001",
  firstName: "João",
  lastName:  "Silva",
  email:     "joao@test.com",
  phone:     "+351912345678",
  createdAt: "2024-01-01T00:00:00Z",
  userId:    null,
};

const CLIENT_B: ClientResponse = {
  id:        "client-0002",
  studioId:  "stud-0001",
  firstName: "Maria",
  lastName:  "Ferreira",
  email:     "maria@test.com",
  phone:     null,
  createdAt: "2024-01-02T00:00:00Z",
  userId:    null,
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/clients", () =>
    HttpResponse.json([CLIENT_A, CLIENT_B]),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Store / render helpers ─────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth: authReducer,
      ui:   uiReducer,
      [clientsApi.reducerPath]: clientsApi.reducer,
    },
    middleware: (gd) => gd().concat(clientsApi.middleware),
    preloadedState: {
      auth: {
        user: { id: "u1", email: "owner@ink.test" },
        token: "fake-token",
        tenantId: "stud-0001",
        role: "owner",
        pendingReferralCode: null,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
      ui: { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null },
    },
  });
}

function renderPage() {
  const store = makeStore();
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={["/clients"]}>
        <Routes>
          <Route path="/clients"     element={<ClientListPage />} />
          <Route path="/clients/:id" element={<div data-testid="client-detail" />} />
          <Route path="/clients/new" element={<div data-testid="client-new" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("ClientListPage", () => {
  it("renders the Clients page heading", async () => {
    renderPage();
    expect(await screen.findByText("Clients", { selector: "span.font-semibold" })).toBeInTheDocument();
  });

  it("does not show client names while loading", () => {
    renderPage();
    expect(screen.queryByText("João Silva")).not.toBeInTheDocument();
  });

  it("renders client full names", async () => {
    renderPage();
    expect(await screen.findByText("João Silva")).toBeInTheDocument();
    expect(screen.getByText("Maria Ferreira")).toBeInTheDocument();
  });

  it("renders initials avatar for each client", async () => {
    renderPage();
    await screen.findByText("João Silva");
    expect(screen.getByText("JS")).toBeInTheDocument();
    expect(screen.getByText("MF")).toBeInTheDocument();
  });

  it("renders the phone number when present", async () => {
    renderPage();
    await screen.findByText("João Silva");
    expect(screen.getByText("+351912345678")).toBeInTheDocument();
  });

  it("renders an accessible em-dash when phone is null", async () => {
    renderPage();
    await screen.findByText("Maria Ferreira");
    expect(screen.getByLabelText("Not provided")).toBeInTheDocument();
  });

  it("clicking a client row navigates to /clients/:id", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("João Silva");

    await user.click(screen.getByText("João Silva"));

    expect(screen.getByTestId("client-detail")).toBeInTheDocument();
  });

  it("search input is present on the page", async () => {
    renderPage();
    await screen.findByText("João Silva");
    expect(screen.getByPlaceholderText(/search/i)).toBeInTheDocument();
  });

  it("shows an error message when clients fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients", () =>
        HttpResponse.json({ message: "Server error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/failed to load/i)).toBeInTheDocument();
  });

  it("shows empty state when no clients are returned", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients", () =>
        HttpResponse.json([]),
      ),
    );
    renderPage();
    await screen.findByText(/no clients/i);
  });

  // ── View action ─────────────────────────────────────────────────────────────

  it("renders a View button for each loaded client", async () => {
    renderPage();
    await screen.findByText("João Silva");

    const viewButtons = screen.getAllByRole("button", { name: /^view$/i });
    expect(viewButtons.length).toBe(2);
  });

  it("clicking the View button navigates to /clients/:id", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("João Silva");

    const viewButtons = screen.getAllByRole("button", { name: /^view$/i });
    await user.click(viewButtons[0]);

    expect(screen.getByTestId("client-detail")).toBeInTheDocument();
  });

  // ── Rich empty state ─────────────────────────────────────────────────────────

  it("shows rich empty state when no clients exist and no search is active", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients", () => HttpResponse.json([])),
    );
    renderPage();

    expect(await screen.findByText("No clients yet")).toBeInTheDocument();
    expect(screen.getByText(/add your first client/i)).toBeInTheDocument();
  });

  it("rich empty state shows a New Client button", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients", () => HttpResponse.json([])),
    );
    renderPage();

    await screen.findByText("No clients yet");
    expect(screen.getByRole("button", { name: /new client/i })).toBeInTheDocument();
  });

  it("rich empty state New Client button navigates to /clients/new", async () => {
    const user = userEvent.setup();
    server.use(
      http.get("http://localhost/api/v1/clients", () => HttpResponse.json([])),
    );
    renderPage();

    await screen.findByText("No clients yet");
    await user.click(screen.getByRole("button", { name: /new client/i }));

    expect(screen.getByTestId("client-new")).toBeInTheDocument();
  });

  it("shows DataTable with emptyMessage when search returns no clients", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients", () => HttpResponse.json([])),
    );
    const user = userEvent.setup();
    renderPage();

    await screen.findByText("No clients yet");
    await user.type(screen.getByPlaceholderText(/search/i), "xyz");

    expect(await screen.findByText(/no clients match/i)).toBeInTheDocument();
    expect(screen.queryByText("No clients yet")).not.toBeInTheDocument();
  });
});
