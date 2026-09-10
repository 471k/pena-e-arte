import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { platformApi } from "@/features/platform/platformApi";
import { ImpersonationBanner } from "@/shared/components/ImpersonationBanner";

const IMPERSONATION = {
  sessionId:  "sess-1",
  studioId:   "studio-1",
  studioName: "Ink & Iron",
  expiresAt:  new Date(Date.now() + 45 * 60_000).toISOString(),
};

const server = setupServer(
  http.post("http://localhost/api/v1/platform/impersonation-sessions/:sessionId/end", () =>
    new HttpResponse(null, { status: 204 })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function makeStore(impersonation: typeof IMPERSONATION | null, impersonationScopeError: string | null = null) {
  return configureStore({
    reducer: {
      auth: authReducer,
      ui:   uiReducer,
      [platformApi.reducerPath]: platformApi.reducer,
    },
    middleware: (gd) => gd().concat(platformApi.middleware),
    preloadedState: {
      auth: {
        user: { id: "admin-1", email: "admin@test.com" }, token: "fake-imp-token",
        refreshToken: null, tenantId: "studio-1", role: "admin",
        pendingReferralCode: null, impersonation,
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
      ui: {
        readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null,
        impersonationScopeError, impersonationSessionExpired: false,
      },
    },
  });
}

function renderBanner(impersonation: typeof IMPERSONATION | null = IMPERSONATION, scopeError: string | null = null) {
  const store = makeStore(impersonation, scopeError);
  render(
    <Provider store={store}>
      <MemoryRouter>
        <ImpersonationBanner />
      </MemoryRouter>
    </Provider>,
  );
  return store;
}

describe("ImpersonationBanner", () => {
  it("renders nothing when there is no active impersonation session", () => {
    renderBanner(null);
    expect(screen.queryByText(/viewing as/i)).not.toBeInTheDocument();
  });

  it("shows 'Viewing as {studio}' when impersonating", () => {
    renderBanner();
    expect(screen.getByText(/viewing as ink & iron/i)).toBeInTheDocument();
  });

  it("has no dismiss control — the persistent banner cannot be closed", () => {
    renderBanner();
    expect(screen.queryByRole("button", { name: /dismiss/i })).not.toBeInTheDocument();
  });

  it("shows an End session button", () => {
    renderBanner();
    expect(screen.getByRole("button", { name: /end session/i })).toBeInTheDocument();
  });

  it("clicking End session calls the end endpoint and clears impersonation state", async () => {
    const user = userEvent.setup();
    const store = renderBanner();

    await user.click(screen.getByRole("button", { name: /end session/i }));

    await waitFor(() => expect(store.getState().auth.impersonation).toBeNull());
    expect(screen.queryByText(/viewing as/i)).not.toBeInTheDocument();
  });

  it("shows a dismissible scope-error notice when a write was blocked", async () => {
    const user = userEvent.setup();
    renderBanner(IMPERSONATION, "This action is not available while impersonating a studio.");

    expect(screen.getByText(/not available while impersonating/i)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /dismiss/i }));
    expect(screen.queryByText(/not available while impersonating/i)).not.toBeInTheDocument();
  });
});
