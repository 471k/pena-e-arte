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
import { servicesApi } from "@/features/services/servicesApi";
import { ServiceListPage } from "@/features/services/components/ServiceListPage";
import type { ServiceResponse } from "@/features/services/service.types";
import { Role } from "@/shared/types/roles";

// ── Seed data ──────────────────────────────────────────────────────────────────

const SERVICE_WITH_DEPOSIT: ServiceResponse = {
  id: "svc-001", studioId: "s-001",
  name: "New Tattoo Session", description: "First session",
  durationMinutes: 90, price: 150, depositAmount: 50,
  isActive: true,
  createdAt: "2024-01-15T10:00:00Z", updatedAt: "2024-01-15T10:00:00Z",
};

const SERVICE_INACTIVE: ServiceResponse = {
  id: "svc-002", studioId: "s-001",
  name: "Retired Service", description: null,
  durationMinutes: 30, price: null, depositAmount: null,
  isActive: false,
  createdAt: "2024-02-01T10:00:00Z", updatedAt: "2024-02-01T10:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/services", () =>
    HttpResponse.json([SERVICE_WITH_DEPOSIT, SERVICE_INACTIVE]),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore(role: Role = Role.Owner) {
  return configureStore({
    reducer: {
      auth:                      authReducer,
      ui:                        uiReducer,
      [servicesApi.reducerPath]: servicesApi.reducer,
    },
    middleware: (gd) => gd().concat(servicesApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "test@test.com" }, token: "fake-token", tenantId: "s-001", role, pendingReferralCode: null, impersonation: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null, impersonationScopeError: null, impersonationSessionExpired: false },
    },
  });
}

function renderPage(role: Role = Role.Owner) {
  render(
    <Provider store={makeStore(role)}>
      <MemoryRouter initialEntries={["/services"]}>
        <Routes>
          <Route path="/services"     element={<ServiceListPage />} />
          <Route path="/services/new" element={<div data-testid="create-page" />} />
          <Route path="/services/:id" element={<div data-testid="detail-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("ServiceListPage", () => {

  it("renders the Services header", () => {
    renderPage();
    expect(screen.getByText("Services")).toBeInTheDocument();
  });

  it("shows an error message when the services fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/services", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText("Failed to load services. Please try again.")).toBeInTheDocument();
  });

  it("shows rich empty state when no services exist", async () => {
    server.use(
      http.get("http://localhost/api/v1/services", () => HttpResponse.json([])),
    );
    renderPage();
    expect(await screen.findByText("No services yet")).toBeInTheDocument();
  });

  it("renders a card for each service returned by the API", async () => {
    renderPage();
    expect(await screen.findByText("New Tattoo Session")).toBeInTheDocument();
    expect(screen.getByText("Retired Service")).toBeInTheDocument();
  });

  it("shows duration, price, and deposit for a service that has them", async () => {
    renderPage();
    await screen.findByText("New Tattoo Session");
    expect(screen.getByText(/90 min · from 150,00\s?€ · 50,00\s?€ deposit/i)).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("shows Inactive badge for an inactive service", async () => {
    renderPage();
    await screen.findByText("Retired Service");
    expect(screen.getByText("Inactive")).toBeInTheDocument();
  });

  it("shows '2 services' count in the header", async () => {
    renderPage();
    expect(await screen.findByText("2 services")).toBeInTheDocument();
  });

  it("uses singular 'service' in the count when only one service exists", async () => {
    server.use(
      http.get("http://localhost/api/v1/services", () => HttpResponse.json([SERVICE_WITH_DEPOSIT])),
    );
    renderPage();
    expect(await screen.findByText("1 service")).toBeInTheDocument();
  });

  it("'New Service' button is visible for the Owner role", async () => {
    renderPage(Role.Owner);
    await screen.findByText("New Tattoo Session");
    expect(screen.getByRole("button", { name: /new service/i })).toBeInTheDocument();
  });

  it("'New Service' button is NOT visible for the Artist role", async () => {
    renderPage(Role.Artist);
    await screen.findByText("New Tattoo Session");
    expect(screen.queryByRole("button", { name: /new service/i })).not.toBeInTheDocument();
  });

  it("'New Service' button navigates to /services/new", async () => {
    const user = userEvent.setup();
    renderPage(Role.Owner);
    await screen.findByText("New Tattoo Session");
    await user.click(screen.getByRole("button", { name: /new service/i }));
    expect(screen.getByTestId("create-page")).toBeInTheDocument();
  });

  it("clicking a service card navigates to the detail page", async () => {
    const user = userEvent.setup();
    renderPage(Role.Owner);
    const link = await screen.findByRole("link", { name: /new tattoo session/i });
    await user.click(link);
    expect(screen.getByTestId("detail-page")).toBeInTheDocument();
  });
});
