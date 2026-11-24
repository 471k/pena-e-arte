import { describe, it, expect, beforeAll, afterEach, afterAll, vi } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { platformApi } from "@/features/platform/platformApi";
import { ImpersonationBanner } from "@/shared/components/ImpersonationBanner";
import { expiryAnnouncement } from "@/shared/utils/supportSessionTime";
import { toast } from "sonner";

const IMPERSONATION = {
  sessionId:  "sess-1",
  studioId:   "studio-1",
  studioName: "Ink & Iron",
  expiresAt:  new Date(Date.now() + 45 * 60_000).toISOString(),
  reason:     "Owner reports deposits not showing" as string | null,
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

  // ── D05: read-only, the reason, and a quiet countdown ──────────────────────────────────────

  it("says the session is read-only and shows the reason given for it", () => {
    renderBanner();
    expect(screen.getByText("Read-only")).toBeInTheDocument();
    expect(screen.getByText("Reason: Owner reports deposits not showing")).toBeInTheDocument();
  });

  it("says when this tab doesn't have the reason, rather than showing nothing", () => {
    renderBanner({ ...IMPERSONATION, reason: null });
    expect(screen.getByText("Reason: not available in this tab")).toBeInTheDocument();
  });

  it("keeps the per-second countdown out of live announcements; only thresholds are announced", () => {
    renderBanner();
    const countdown = screen.getByText(/\d+:\d{2} left/);
    expect(countdown).toHaveAttribute("aria-hidden", "true");
    expect(countdown.closest("[aria-live]")).toBeNull();
    // 45 minutes left: below no threshold, so the live region is empty.
    const live = document.querySelector("[aria-live='polite']");
    expect(live).not.toBeNull();
    expect(live).toHaveTextContent("");
  });

  it("announces only at 10, 5 and 1 minutes and at expiry", () => {
    expect(expiryAnnouncement(11 * 60_000)).toBe("");
    expect(expiryAnnouncement(10 * 60_000)).toMatch(/10 minutes left/);
    expect(expiryAnnouncement(6 * 60_000)).toMatch(/10 minutes left/);
    expect(expiryAnnouncement(5 * 60_000)).toMatch(/5 minutes left/);
    expect(expiryAnnouncement(30_000)).toMatch(/1 minute left/);
    expect(expiryAnnouncement(0)).toMatch(/expiring now/);
    // The text only changes at a threshold, so a polite region re-announces it only then.
    const seconds = Array.from({ length: 12 * 60 }, (_, i) => expiryAnnouncement((12 * 60 - i) * 1000));
    const changes = seconds.filter((t, i) => i > 0 && t !== seconds[i - 1]).length;
    expect(changes).toBe(3);
  });
});

describe("D04: ending a session", () => {
  function renderWithStudioRoute() {
    const store = makeStore(IMPERSONATION, null);
    function StudioPage() {
      const location = useLocation();
      const state = location.state as { receipt?: { summary: string; action: string } } | null;
      return <p data-testid="studio-page">{state?.receipt ? `${state.receipt.summary} | ${state.receipt.action}` : "no receipt"}</p>;
    }
    render(
      <Provider store={store}>
        <MemoryRouter initialEntries={["/dashboard"]}>
          <ImpersonationBanner />
          <Routes>
            <Route path="/dashboard" element={<p>owner dashboard</p>} />
            <Route path="/platform/studios/:studioId" element={<StudioPage />} />
          </Routes>
        </MemoryRouter>
      </Provider>,
    );
    return store;
  }

  it("lands on the studio's page with a receipt for the ended session", async () => {
    const user = userEvent.setup();
    renderWithStudioRoute();
    await user.click(screen.getByRole("button", { name: /end session/i }));
    expect(await screen.findByTestId("studio-page"))
      .toHaveTextContent("The support session in Ink & Iron has ended. | ImpersonationSession.Ended");
  });

  it("if the server can't record the end, says why, still hands the admin back, and claims no receipt", async () => {
    const error = vi.spyOn(toast, "error");
    server.use(http.post("http://localhost/api/v1/platform/impersonation-sessions/:sessionId/end", () =>
      HttpResponse.json({ message: "This session has already ended." }, { status: 409 })));
    const user = userEvent.setup();
    const store = renderWithStudioRoute();
    await user.click(screen.getByRole("button", { name: /end session/i }));
    await waitFor(() => expect(store.getState().auth.impersonation).toBeNull());
    expect(error).toHaveBeenCalledWith("This session has already ended.");
    expect(await screen.findByTestId("studio-page")).toHaveTextContent("no receipt");
    error.mockRestore();
  });
});
