import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, fireEvent } from "@testing-library/react";
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
import { CreateServicePage } from "@/features/services/components/CreateServicePage";
import type { ServiceResponse } from "@/features/services/service.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const CREATED_SERVICE: ServiceResponse = {
  id: "svc-new", studioId: "s-001",
  name: "New Tattoo Session", description: null,
  durationMinutes: 90, price: null, depositAmount: null,
  isActive: true,
  createdAt: "2024-06-01T10:00:00Z", updatedAt: "2024-06-01T10:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.post("http://localhost/api/v1/services", () =>
    HttpResponse.json(CREATED_SERVICE, { status: 201 }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                      authReducer,
      ui:                        uiReducer,
      [servicesApi.reducerPath]: servicesApi.reducer,
    },
    middleware: (gd) => gd().concat(servicesApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "owner@test.com" }, token: "fake-token", tenantId: "s-001", role: "owner", pendingReferralCode: null, impersonation: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null, impersonationScopeError: null, impersonationSessionExpired: false },
    },
  });
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <Toaster />
      <MemoryRouter initialEntries={["/services/new"]}>
        <Routes>
          <Route path="/services/new" element={<CreateServicePage />} />
          <Route path="/services"     element={<div data-testid="list-page" />} />
          <Route path="/services/:id" element={<div data-testid="detail-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("CreateServicePage", () => {

  it("renders the 'New Service' heading", () => {
    renderPage();
    expect(screen.getByText("New Service")).toBeInTheDocument();
  });

  it("renders name, description, duration, price, deposit, and active fields", () => {
    renderPage();
    expect(screen.getByLabelText("Service name")).toBeInTheDocument();
    expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/duration/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/starting price/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/deposit/i)).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("defaults duration to 60 minutes", () => {
    renderPage();
    expect(screen.getByLabelText(/duration/i)).toHaveValue(60);
  });

  it("shows a validation error when submitting an empty name", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.clear(screen.getByLabelText(/duration/i));
    await user.type(screen.getByLabelText(/duration/i), "60");
    await user.click(screen.getByRole("button", { name: /create service/i }));
    expect(await screen.findByText("Name is required")).toBeInTheDocument();
  });

  it("rejects a duration below 5 minutes", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText("Service name"), "Touch-Up");
    // userEvent.clear()/selection is unreliable on type="number" inputs in jsdom — set the
    // value directly instead (same reason DepositRule's tests never needed this: no field
    // there both defaults to a non-empty value AND needs a boundary re-typed).
    fireEvent.change(screen.getByLabelText(/duration/i), { target: { value: "2" } });
    await user.click(screen.getByRole("button", { name: /create service/i }));
    expect(await screen.findByText("Must be at least 5 minutes")).toBeInTheDocument();
  });

  it("rejects a duration above 600 minutes", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText("Service name"), "Marathon Session");
    fireEvent.change(screen.getByLabelText(/duration/i), { target: { value: "601" } });
    await user.click(screen.getByRole("button", { name: /create service/i }));
    expect(await screen.findByText("Must be 600 minutes or less")).toBeInTheDocument();
  });

  it("creates a service and navigates to its detail page", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText("Service name"), "New Tattoo Session");
    await user.click(screen.getByRole("button", { name: /create service/i }));
    expect(await screen.findByTestId("detail-page")).toBeInTheDocument();
  });

  it("submits price and deposit fields when provided", async () => {
    let capturedBody: Record<string, unknown> | undefined;
    server.use(
      http.post("http://localhost/api/v1/services", async ({ request }) => {
        capturedBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(CREATED_SERVICE, { status: 201 });
      }),
    );
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText("Service name"), "New Tattoo Session");
    await user.type(screen.getByLabelText(/starting price/i), "150");
    await user.type(screen.getByLabelText(/deposit/i), "50");
    await user.click(screen.getByRole("button", { name: /create service/i }));
    await screen.findByTestId("detail-page");
    expect(capturedBody?.price).toBe(150);
    expect(capturedBody?.depositAmount).toBe(50);
  });

  it("shows a success toast after creating a service", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText("Service name"), "New Tattoo Session");
    await user.click(screen.getByRole("button", { name: /create service/i }));
    expect(await screen.findByText("Service created.")).toBeInTheDocument();
  });

  it("shows an error toast and stays on the page when the API call fails", async () => {
    server.use(
      http.post("http://localhost/api/v1/services", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText("Service name"), "New Tattoo Session");
    await user.click(screen.getByRole("button", { name: /create service/i }));
    expect(await screen.findByText("Failed to create service.")).toBeInTheDocument();
    expect(screen.queryByTestId("detail-page")).not.toBeInTheDocument();
  });

  it("'Services' back button navigates to /services", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole("button", { name: /^services$/i }));
    expect(screen.getByTestId("list-page")).toBeInTheDocument();
  });
});
