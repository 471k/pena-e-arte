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
import { designsApi } from "@/features/designs/designsApi";
import { artistsApi } from "@/features/artists/artistsApi";
import { clientsApi } from "@/features/clients/clientsApi";
import { CreateDesignPage } from "@/features/designs/components/CreateDesignPage";
import type { ArtistResponse } from "@/features/artists/artistsApi";
import type { ClientResponse } from "@/features/clients/clientsApi";
import type { DesignResponse } from "@/features/designs/design.types";

// ── Seed data ──────────────────────────────────────────────────────────────────

const ARTIST: ArtistResponse = {
  id:              "a-001",
  studioId:        "s-001",
  firstName:       "Ana",
  lastName:        "Costa",
  email:           "ana@ink.test",
  specializations: null,
  hourlyRate:      null,
  portfolioImages: [],
  slug: null,
  userId:          null,
  createdAt:       "2024-01-01T00:00:00Z",
  updatedAt:       "2024-01-01T00:00:00Z",
};

const CLIENT: ClientResponse = {
  id:        "c-001",
  studioId:  "s-001",
  firstName: "João",
  lastName:  "Silva",
  email:     "joao@test.com",
  phone:     null,
  createdAt: "2024-01-01T00:00:00Z",
  userId:    null,
};

const CREATED_DESIGN: DesignResponse = {
  id:          "d-new",
  studioId:    "s-001",
  clientId:    "c-001",
  artistId:    "a-001",
  title:       "Japanese Sleeve",
  description: null,
  createdAt:   "2024-06-01T10:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/artists", () => HttpResponse.json([ARTIST])),
  http.get("http://localhost/api/v1/clients", () => HttpResponse.json([CLIENT])),
  http.post("http://localhost/api/v1/designs",  () => HttpResponse.json(CREATED_DESIGN, { status: 201 })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore() {
  return configureStore({
    reducer: {
      auth:                          authReducer,
      ui:                            uiReducer,
      [designsApi.reducerPath]:      designsApi.reducer,
      [artistsApi.reducerPath]:      artistsApi.reducer,
      [clientsApi.reducerPath]:      clientsApi.reducer,
    },
    middleware: (gd) =>
      gd().concat(designsApi.middleware, artistsApi.middleware, clientsApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "artist@ink.test" }, token: "fake-token", tenantId: "s-001", role: "artist", pendingReferralCode: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false },
    },
  });
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter initialEntries={["/designs/new"]}>
        <Routes>
          <Route path="/designs/new" element={<CreateDesignPage />} />
          <Route path="/designs"     element={<div data-testid="designs-list" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("CreateDesignPage", () => {

  it("renders the 'New Design' heading", () => {
    renderPage();
    expect(screen.getByText("New Design")).toBeInTheDocument();
  });

  it("renders Client, Artist, Title and Description fields", async () => {
    renderPage();
    expect(screen.getByLabelText("Client")).toBeInTheDocument();
    expect(screen.getByLabelText("Artist")).toBeInTheDocument();
    expect(screen.getByLabelText("Title")).toBeInTheDocument();
    expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
  });

  it("populates the Client dropdown with API results", async () => {
    renderPage();
    expect(await screen.findByRole("option", { name: /joão silva/i })).toBeInTheDocument();
  });

  it("populates the Artist dropdown with API results", async () => {
    renderPage();
    expect(await screen.findByRole("option", { name: /ana costa/i })).toBeInTheDocument();
  });

  it("shows validation error when submitting with no Client selected", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("option", { name: /joão silva/i });
    await user.click(screen.getByRole("button", { name: /create design/i }));
    // The placeholder <option> also reads "Select a client", so target the error <p> specifically.
    expect(await screen.findByText("Select a client", { selector: "p" })).toBeInTheDocument();
  });

  it("shows validation error when Title is empty", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("option", { name: /joão silva/i });
    await user.selectOptions(screen.getByLabelText("Client"), "c-001");
    await user.selectOptions(screen.getByLabelText("Artist"), "a-001");
    await user.click(screen.getByRole("button", { name: /create design/i }));
    expect(await screen.findByText("Title is required")).toBeInTheDocument();
  });

  it("navigates to /designs after a successful submission", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("option", { name: /joão silva/i });

    await user.selectOptions(screen.getByLabelText("Client"), "c-001");
    await user.selectOptions(screen.getByLabelText("Artist"), "a-001");
    await user.type(screen.getByLabelText("Title"), "Japanese Sleeve");
    await user.click(screen.getByRole("button", { name: /create design/i }));

    expect(await screen.findByTestId("designs-list")).toBeInTheDocument();
  });

  it("disables the back button and submit while the mutation is in-flight", async () => {
    let resolvePost!: (v: Response) => void;
    server.use(
      http.post("http://localhost/api/v1/designs", () =>
        new Promise<Response>((r) => { resolvePost = r; }),
      ),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("option", { name: /joão silva/i });

    await user.selectOptions(screen.getByLabelText("Client"), "c-001");
    await user.selectOptions(screen.getByLabelText("Artist"), "a-001");
    await user.type(screen.getByLabelText("Title"), "In Flight");
    await user.click(screen.getByRole("button", { name: /create design/i }));

    expect(await screen.findByText(/creating…/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /designs/i })).toBeDisabled();

    resolvePost(HttpResponse.json(CREATED_DESIGN, { status: 201 }) as unknown as Response);
  });

  it("shows client load error message when the clients fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/clients", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText("Failed to load clients")).toBeInTheDocument();
  });
});
