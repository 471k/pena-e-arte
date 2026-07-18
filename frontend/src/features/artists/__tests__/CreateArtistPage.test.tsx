import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { toast } from "sonner";

import authReducer from "@/features/auth/authSlice";
import { artistsApi } from "@/features/artists/artistsApi";
import { billingApi } from "@/features/billing/billingApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { CreateArtistPage } from "@/features/artists/components/CreateArtistPage";

vi.mock("sonner", () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

const server = setupServer();

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); vi.clearAllMocks(); });
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth:                      authReducer,
      [artistsApi.reducerPath]:  artistsApi.reducer,
      [billingApi.reducerPath]:  billingApi.reducer,
      [studiosApi.reducerPath]:  studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(artistsApi.middleware, billingApi.middleware, studiosApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "owner@test.com" }, token: "fake", tenantId: "t1", role: "artist", pendingReferralCode: null } as any,
    },
  });
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <CreateArtistPage />
      </MemoryRouter>
    </Provider>,
  );
}

async function fillAndSubmit(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(/first name/i), "Rui");
  await user.type(screen.getByLabelText(/last name/i), "Tavares");
  await user.type(screen.getByLabelText(/email/i), "rui@studio.com");
  await user.click(screen.getByRole("button", { name: /create artist/i }));
}

describe("CreateArtistPage", () => {
  it("shows a success toast and navigates on successful creation", async () => {
    server.use(
      http.post("http://localhost/api/v1/artists", () =>
        HttpResponse.json({
          id: "artist-1", studioId: "t1", userId: "u9", firstName: "Rui", lastName: "Tavares",
          email: "rui@studio.com", specializations: null, hourlyRate: null, isActive: true,
          avatarUrl: null, portfolioImages: [], slug: "rui-tavares",
          createdAt: "2026-01-01T00:00:00Z", updatedAt: "2026-01-01T00:00:00Z",
        }),
      ),
    );
    const user = userEvent.setup();
    renderPage();

    await fillAndSubmit(user);

    expect(toast.success).toHaveBeenCalledWith("Artist created.");
  });

  it("shows the real backend message on PLAN_LIMIT_EXCEEDED, not a generic string", async () => {
    server.use(
      http.post("http://localhost/api/v1/artists", () =>
        HttpResponse.json(
          { status: 403, message: "This studio's plan allows up to 6 artists. Upgrade the plan to continue.", code: "PLAN_LIMIT_EXCEEDED" },
          { status: 403 },
        ),
      ),
    );
    const user = userEvent.setup();
    renderPage();

    await fillAndSubmit(user);

    expect(toast.error).toHaveBeenCalledWith(
      "This studio's plan allows up to 6 artists. Upgrade the plan to continue.",
    );
    expect(toast.error).not.toHaveBeenCalledWith("Failed to create artist.");
  });

  it("falls back to the generic message when the backend sends no message", async () => {
    server.use(
      http.post("http://localhost/api/v1/artists", () =>
        HttpResponse.json({ status: 500 }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderPage();

    await fillAndSubmit(user);

    expect(toast.error).toHaveBeenCalledWith("Failed to create artist.");
  });
});
