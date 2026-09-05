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
import { artistsApi } from "@/features/artists/artistsApi";
import { CreateClientPage } from "@/features/clients/components/CreateClientPage";
import type { ArtistResponse } from "@/features/artists/artistsApi";

// ── Seed data ──────────────────────────────────────────────────────────────────

const ARTIST: ArtistResponse = {
  id:              "a-001",
  studioId:        "s-001",
  userId:          null,
  firstName:       "Ana",
  lastName:        "Costa",
  email:           "ana@ink.test",
  specializations: null,
  hourlyRate:      null,
  isActive:        true,
  avatarUrl:       null,
  portfolioImages: [],
  slug:            null,
  createdAt:       "2024-01-01T00:00:00Z",
  updatedAt:       "2024-01-01T00:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/artists", () => HttpResponse.json([ARTIST])),
  http.get("http://localhost/api/v1/artists/me", () => HttpResponse.json(ARTIST)),
  http.post("http://localhost/api/v1/clients", async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>;
    return HttpResponse.json({
      id:         "new-client-001",
      studioId:   "stud-0001",
      firstName:  body.firstName,
      lastName:   body.lastName,
      email:      body.email,
      phone:      body.phone ?? null,
      createdAt:  "2026-06-15T09:00:00.000Z",
      userId:     null,
      artistId:   body.artistId ?? null,
      artistName: "Ana Costa",
    });
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore(role: "owner" | "artist" = "owner") {
  return configureStore({
    reducer: {
      auth: authReducer,
      [clientsApi.reducerPath]: clientsApi.reducer,
      [artistsApi.reducerPath]: artistsApi.reducer,
    },
    middleware: (gd) => gd().concat(clientsApi.middleware, artistsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "owner@test.com" }, token: "fake", tenantId: "t1", role } as any,
    },
  });
}

function renderPage(role: "owner" | "artist" = "owner") {
  render(
    <Provider store={makeStore(role)}>
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

async function selectArtist(user: ReturnType<typeof userEvent.setup>) {
  await user.click(await screen.findByRole("combobox", { name: /select artist/i }));
  await user.click(await screen.findByRole("option", { name: "Ana Costa" }));
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("CreateClientPage", () => {
  it("renders the form fields", async () => {
    renderPage();
    expect(screen.getByLabelText(/first name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/last name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^email/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/phone/i)).toBeInTheDocument();
    expect(await screen.findByRole("combobox", { name: /select artist/i })).toBeInTheDocument();
  });

  it("shows validation errors when submitting empty form", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("combobox", { name: /select artist/i });
    await user.click(screen.getByRole("button", { name: /create client/i }));
    expect(await screen.findByText("First name is required")).toBeInTheDocument();
    expect(screen.getByText("Last name is required")).toBeInTheDocument();
    expect(screen.getByText("Invalid email")).toBeInTheDocument();
    expect(screen.getByText("Select an artist")).toBeInTheDocument();
  });

  // These tests each fill the full form (including the artist Select) and submit —
  // heavy enough interaction sequences that this sandbox's known CPU-contention
  // flakiness (see src/test/setup.ts's asyncUtilTimeout comment) can push them past the
  // 10s default under load even though nothing is actually broken; confirmed by
  // re-running in isolation with a raised timeout and watching them pass, 2026-09-05.
  it("does not require phone", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/first name/i), "Ana");
    await user.type(screen.getByLabelText(/last name/i), "Costa");
    await user.type(screen.getByLabelText(/^email/i), "ana@example.com");
    await selectArtist(user);
    await user.click(screen.getByRole("button", { name: /create client/i }));
    expect(await screen.findByTestId("detail-page")).toBeInTheDocument();
  }, 20000);

  it("submitting a valid form navigates to the new client's detail page", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/first name/i), "Ana");
    await user.type(screen.getByLabelText(/last name/i), "Costa");
    await user.type(screen.getByLabelText(/^email/i), "ana@example.com");
    await user.type(screen.getByLabelText(/phone/i), "912000000");
    await selectArtist(user);
    await user.click(screen.getByRole("button", { name: /create client/i }));
    expect(await screen.findByTestId("detail-page")).toBeInTheDocument();
  }, 20000);

  it("typing an invalid phone number and submitting shows the phone error and blocks submission", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/first name/i), "Ana");
    await user.type(screen.getByLabelText(/last name/i), "Costa");
    await user.type(screen.getByLabelText(/^email/i), "ana@example.com");
    await user.type(screen.getByLabelText(/phone/i), "912");
    await selectArtist(user);
    await user.click(screen.getByRole("button", { name: /create client/i }));
    expect(await screen.findByText(/enter a valid phone number/i)).toBeInTheDocument();
    expect(screen.queryByTestId("detail-page")).not.toBeInTheDocument();
  }, 20000);

  it("typing a valid phone number submits with the correct E.164 value", async () => {
    let capturedPhone: unknown;
    server.use(
      http.post("http://localhost/api/v1/clients", async ({ request }) => {
        const body = (await request.json()) as Record<string, unknown>;
        capturedPhone = body.phone;
        return HttpResponse.json({
          id: "new-client-001", studioId: "stud-0001", firstName: body.firstName,
          lastName: body.lastName, email: body.email, phone: body.phone ?? null,
          createdAt: "2026-06-15T09:00:00.000Z", userId: null,
          artistId: body.artistId ?? null, artistName: "Ana Costa",
        });
      }),
    );
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/first name/i), "Ana");
    await user.type(screen.getByLabelText(/last name/i), "Costa");
    await user.type(screen.getByLabelText(/^email/i), "ana@example.com");
    await user.type(screen.getByLabelText(/phone/i), "912345678");
    await selectArtist(user);
    await user.click(screen.getByRole("button", { name: /create client/i }));
    await screen.findByTestId("detail-page");
    expect(capturedPhone).toBe("+351912345678");
  }, 20000);

  it("shows a success toast after creating a client", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.type(screen.getByLabelText(/first name/i), "Ana");
    await user.type(screen.getByLabelText(/last name/i), "Costa");
    await user.type(screen.getByLabelText(/^email/i), "ana@example.com");
    await selectArtist(user);
    await user.click(screen.getByRole("button", { name: /create client/i }));
    expect(await screen.findByText("Client created.")).toBeInTheDocument();
  }, 20000);

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
    await selectArtist(user);
    await user.click(screen.getByRole("button", { name: /create client/i }));
    expect(await screen.findByText("Failed to create client.")).toBeInTheDocument();
    expect(screen.queryByTestId("detail-page")).not.toBeInTheDocument();
  }, 20000);

  it("'Clients' back button navigates to /clients", async () => {
    const user = userEvent.setup();
    renderPage();
    await user.click(screen.getByRole("button", { name: /clients/i }));
    expect(screen.getByTestId("list-page")).toBeInTheDocument();
  });

  it("artist role does not see the Artist select and submits successfully via the hidden auto-fill", async () => {
    const user = userEvent.setup();
    renderPage("artist");
    await screen.findByLabelText(/first name/i);
    expect(screen.queryByRole("combobox", { name: /select artist/i })).not.toBeInTheDocument();
    await user.type(screen.getByLabelText(/first name/i), "Ana");
    await user.type(screen.getByLabelText(/last name/i), "Costa");
    await user.type(screen.getByLabelText(/^email/i), "ana@example.com");
    await user.click(screen.getByRole("button", { name: /create client/i }));
    expect(await screen.findByTestId("detail-page")).toBeInTheDocument();
  }, 20000);
});
