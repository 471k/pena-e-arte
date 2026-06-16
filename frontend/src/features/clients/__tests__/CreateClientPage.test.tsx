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
import { clientsApi } from "@/features/clients/clientsApi";
import { CreateClientPage } from "@/features/clients/components/CreateClientPage";

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.post("http://localhost/api/v1/clients", async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    return HttpResponse.json({
      id:        "new-client-001",
      studioId:  "stud-0001",
      firstName: body.firstName,
      lastName:  body.lastName,
      email:     body.email,
      phone:     body.phone ?? null,
      createdAt: "2026-06-15T09:00:00.000Z",
      userId:    null,
    });
  }),
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
      auth: { user: { id: "u1", email: "owner@test.com" }, token: "fake", tenantId: "t1", role: "owner" } as any,
    },
  });
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <Toaster />
      <MemoryRouter initialEntries={["/clients/new"]}>
        <Routes>
          <Route path="/clients/new" element={<CreateClientPage />} />
          <Route path="/clients" element={<div data-testid="list-page" />} />
          <Route path="/clients/:id" element={<div data-testid="detail-page" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("CreateClientPage", () => {
  it("renders the form fields", () => {
    renderPage();
    expect(screen.getByLabelText(/first name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/last name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^email/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/phone/i)).toBeInTheDocument();
  });

  it("shows validation errors when submitting empty form", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole("button", { name: /create client/i }));
    expect(await screen.findByText("First name is required")).toBeInTheDocument();
    expect(screen.getByText("Last name is required")).toBeInTheDocument();
    expect(screen.getByText("Invalid email")).toBeInTheDocument();
  });

  it("does not require phone", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/first name/i), "Ana");
    await user.type(screen.getByLabelText(/last name/i), "Costa");
    await user.type(screen.getByLabelText(/^email/i), "ana@example.com");
    await user.click(screen.getByRole("button", { name: /create client/i }));
    expect(await screen.findByTestId("detail-page")).toBeInTheDocument();
  });

  it("submitting a valid form navigates to the new client's detail page", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/first name/i), "Ana");
    await user.type(screen.getByLabelText(/last name/i), "Costa");
    await user.type(screen.getByLabelText(/^email/i), "ana@example.com");
    await user.type(screen.getByLabelText(/phone/i), "+351 912 000 000");
    await user.click(screen.getByRole("button", { name: /create client/i }));
    expect(await screen.findByTestId("detail-page")).toBeInTheDocument();
  });

  it("shows a success toast after creating a client", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/first name/i), "Ana");
    await user.type(screen.getByLabelText(/last name/i), "Costa");
    await user.type(screen.getByLabelText(/^email/i), "ana@example.com");
    await user.click(screen.getByRole("button", { name: /create client/i }));
    expect(await screen.findByText("Client created.")).toBeInTheDocument();
  });

  it("shows an error toast when the API call fails", async () => {
    server.use(
      http.post("http://localhost/api/v1/clients", () =>
        HttpResponse.json({ message: "Email already in use" }, { status: 422 }),
      ),
    );
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/first name/i), "Ana");
    await user.type(screen.getByLabelText(/last name/i), "Costa");
    await user.type(screen.getByLabelText(/^email/i), "ana@example.com");
    await user.click(screen.getByRole("button", { name: /create client/i }));
    expect(await screen.findByText("Failed to create client.")).toBeInTheDocument();
    expect(screen.queryByTestId("detail-page")).not.toBeInTheDocument();
  });

  it("'Clients' back button navigates to /clients", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole("button", { name: /clients/i }));
    expect(screen.getByTestId("list-page")).toBeInTheDocument();
  });
});
