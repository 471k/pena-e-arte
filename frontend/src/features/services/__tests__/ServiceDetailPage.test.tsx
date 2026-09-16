import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { Toaster } from "sonner";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { servicesApi } from "@/features/services/servicesApi";
import { ServiceDetailPage } from "@/features/services/components/ServiceDetailPage";
import type { ServiceResponse } from "@/features/services/service.types";
import { Role } from "@/shared/types/roles";

// ── Seed data ──────────────────────────────────────────────────────────────────

const SERVICE_ID = "svc-001";

const SERVICE: ServiceResponse = {
  id: SERVICE_ID, studioId: "s-001",
  name: "New Tattoo Session", description: "Full session",
  durationMinutes: 90, price: 150, depositAmount: 50,
  isActive: true,
  createdAt: "2024-01-15T10:00:00Z", updatedAt: "2024-01-15T10:00:00Z",
};

const UPDATED_SERVICE: ServiceResponse = {
  ...SERVICE,
  name: "Renamed Service",
  durationMinutes: 60,
  updatedAt: "2024-03-01T10:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/services/:id", () => HttpResponse.json(SERVICE)),
  http.put("http://localhost/api/v1/services/:id", () => HttpResponse.json(UPDATED_SERVICE)),
  http.delete("http://localhost/api/v1/services/:id", () => new HttpResponse(null, { status: 204 })),
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

function renderPage(role: Role = Role.Owner, serviceId = SERVICE_ID) {
  render(
    <Provider store={makeStore(role)}>
      <Toaster />
      <MemoryRouter initialEntries={[`/services/${serviceId}`]}>
        <Routes>
          <Route path="/services/:id" element={<ServiceDetailPage />} />
          <Route path="/services"     element={<div data-testid="list-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("ServiceDetailPage", () => {

  // ── Loading / error ────────────────────────────────────────────────────────

  it("shows a loading skeleton while the service is fetching", () => {
    renderPage();
    expect(screen.getByLabelText(/loading service/i)).toBeInTheDocument();
  });

  it("shows a not-found message when the service fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/services/:id", () =>
        HttpResponse.json({ message: "error" }, { status: 404 }),
      ),
    );
    renderPage();
    expect(await screen.findByText("Service not found.")).toBeInTheDocument();
  });

  // ── View mode ─────────────────────────────────────────────────────────────

  it("renders the service name and Active badge", async () => {
    renderPage();
    expect(await screen.findByText("New Tattoo Session")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("renders description, duration, price, and deposit", async () => {
    renderPage();
    await screen.findByText("New Tattoo Session");
    expect(screen.getByText("Full session")).toBeInTheDocument();
    expect(screen.getByText("90 minutes")).toBeInTheDocument();
    expect(screen.getByText(/price: from 150,00\s?€/i)).toBeInTheDocument();
    expect(screen.getByText(/deposit: 50,00\s?€ \(overrides the studio's deposit rule\)/i)).toBeInTheDocument();
  });

  it("shows 'not shown' and studio deposit-rule fallback text when price/deposit are unset", async () => {
    server.use(
      http.get("http://localhost/api/v1/services/:id", () =>
        HttpResponse.json({ ...SERVICE, price: null, depositAmount: null }),
      ),
    );
    renderPage();
    await screen.findByText("New Tattoo Session");
    expect(screen.getByText(/price: not shown/i)).toBeInTheDocument();
    expect(screen.getByText(/deposit: uses the studio's deposit rule/i)).toBeInTheDocument();
  });

  it("shows Inactive badge for an inactive service", async () => {
    server.use(
      http.get("http://localhost/api/v1/services/:id", () =>
        HttpResponse.json({ ...SERVICE, isActive: false }),
      ),
    );
    renderPage();
    await screen.findByText("New Tattoo Session");
    expect(screen.getByText("Inactive")).toBeInTheDocument();
  });

  // ── Role-gated actions ───────────────────────────────────────────────────────

  it("Edit and Delete buttons are visible for the Owner role", async () => {
    renderPage(Role.Owner);
    await screen.findByText("New Tattoo Session");
    expect(screen.getByRole("button", { name: /edit/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /delete/i })).toBeInTheDocument();
  });

  it("Edit and Delete buttons are NOT visible for the Artist role", async () => {
    renderPage(Role.Artist);
    await screen.findByText("New Tattoo Session");
    expect(screen.queryByRole("button", { name: /edit/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /delete/i })).not.toBeInTheDocument();
  });

  // ── Edit flow ─────────────────────────────────────────────────────────────

  it("clicking Edit shows the edit form pre-filled with the service's values", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /edit/i }));
    expect(screen.getByLabelText("Service name")).toHaveValue("New Tattoo Session");
    expect(screen.getByLabelText(/duration/i)).toHaveValue(90);
    expect(screen.getByLabelText(/starting price/i)).toHaveValue(150);
    expect(screen.getByLabelText(/deposit/i)).toHaveValue(50);
  });

  it("Cancel exits the edit form without saving", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /edit/i }));
    await user.click(screen.getByRole("button", { name: /cancel/i }));
    expect(screen.getByText("New Tattoo Session")).toBeInTheDocument();
    expect(screen.queryByLabelText("Service name")).not.toBeInTheDocument();
  });

  it("saving valid changes shows a success toast and returns to view mode", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /edit/i }));
    await user.clear(screen.getByLabelText("Service name"));
    await user.type(screen.getByLabelText("Service name"), "Renamed Service");
    await user.click(screen.getByRole("button", { name: /save changes/i }));
    expect(await screen.findByText("Service updated.")).toBeInTheDocument();
    expect(screen.queryByLabelText("Service name")).not.toBeInTheDocument();
  });

  it("shows an error toast and stays in edit mode when the update fails", async () => {
    server.use(
      http.put("http://localhost/api/v1/services/:id", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /edit/i }));
    await user.click(screen.getByRole("button", { name: /save changes/i }));
    expect(await screen.findByText("Failed to update service.")).toBeInTheDocument();
    expect(screen.getByLabelText("Service name")).toBeInTheDocument();
  });

  // ── Delete flow ──────────────────────────────────────────────────────────────

  it("clicking Delete shows a delete confirmation", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /delete/i }));
    expect(screen.getByText('Delete "New Tattoo Session"?')).toBeInTheDocument();
    expect(screen.getByText(/existing appointments that used this service keep/i)).toBeInTheDocument();
  });

  it("confirming delete navigates back to the list", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /delete/i }));
    await user.click(screen.getByRole("button", { name: /^delete$/i }));
    expect(await screen.findByTestId("list-page")).toBeInTheDocument();
  });

  it("Cancel on the delete confirmation returns to view mode", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /delete/i }));
    await user.click(screen.getByRole("button", { name: /cancel/i }));
    expect(screen.queryByText('Delete "New Tattoo Session"?')).not.toBeInTheDocument();
    expect(screen.getByText("New Tattoo Session")).toBeInTheDocument();
  });

  it("shows an error toast and stays on the confirmation when delete fails", async () => {
    server.use(
      http.delete("http://localhost/api/v1/services/:id", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /delete/i }));
    await user.click(screen.getByRole("button", { name: /^delete$/i }));
    expect(await screen.findByText("Failed to delete service.")).toBeInTheDocument();
    expect(screen.getByText('Delete "New Tattoo Session"?')).toBeInTheDocument();
  });

  // ── Back navigation ──────────────────────────────────────────────────────────

  it("'Services' back button navigates to /services", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("New Tattoo Session");
    await user.click(screen.getByRole("button", { name: /^services$/i }));
    expect(screen.getByTestId("list-page")).toBeInTheDocument();
  });
});
