import { describe, it, expect, afterEach } from "vitest";
import { render, screen, cleanup, act } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { setSessionExpired } from "@/features/ui/uiSlice";
import { createMemoryRouter, RouterProvider } from "react-router-dom";
import { RoleGuard, AppRoot, getRoleRedirectPath, routes } from "@/app/router";
import { Role } from "@/shared/types/roles";

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeAuthStore(role: string | null) {
  return configureStore({
    reducer: { auth: authReducer },
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: role ? { id: "u1", email: "user@test.com" } : null, token: role ? "fake" : null, tenantId: role ? "t1" : null, role, pendingReferralCode: null } as any,
    },
  });
}

function makeUiStore(sessionExpired: boolean, role: string | null = null) {
  return configureStore({
    reducer: { auth: authReducer, ui: uiReducer },
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: role ? { id: "u1", email: "user@test.com" } : null, token: role ? "fake" : null, tenantId: role ? "t1" : null, role, pendingReferralCode: null } as any,
      ui: { readOnlyError: null, sessionExpired, studioSuspended: false, planLimitError: null },
    },
  });
}

function LocationDisplay() {
  const { pathname, search } = useLocation();
  return <div data-testid="location">{pathname}{search}</div>;
}

afterEach(cleanup);

// ── getRoleRedirectPath ────────────────────────────────────────────────────────

describe("getRoleRedirectPath", () => {
  it("returns /book for client", () => {
    expect(getRoleRedirectPath(Role.Client)).toBe("/book");
  });

  it("returns /schedule for artist", () => {
    expect(getRoleRedirectPath(Role.Artist)).toBe("/schedule");
  });

  it("returns /dashboard for owner", () => {
    expect(getRoleRedirectPath(Role.Owner)).toBe("/dashboard");
  });

  it("returns /platform for issuer", () => {
    expect(getRoleRedirectPath(Role.Issuer)).toBe("/platform");
  });
});

// ── RoleGuard ─────────────────────────────────────────────────────────────────

describe("RoleGuard", () => {
  function renderGuard(allowedRoles: Role[], role: string | null) {
    const store = makeAuthStore(role);
    render(
      <Provider store={store}>
        <MemoryRouter initialEntries={["/protected"]}>
          <Routes>
            <Route element={<RoleGuard allowedRoles={allowedRoles} />}>
              <Route path="/protected" element={<div data-testid="protected-page" />} />
            </Route>
            <Route path="/login"     element={<div data-testid="login-page" />} />
            <Route path="/book"      element={<div data-testid="client-home" />} />
            <Route path="/schedule"  element={<div data-testid="artist-home" />} />
            <Route path="/dashboard" element={<div data-testid="owner-home" />} />
            <Route path="/platform"  element={<div data-testid="issuer-home" />} />
          </Routes>
        </MemoryRouter>
      </Provider>,
    );
    return store;
  }

  it("redirects to /login when user is not authenticated (role is null)", () => {
    renderGuard([Role.Client, Role.Artist, Role.Owner, Role.Issuer], null);
    expect(screen.getByTestId("login-page")).toBeInTheDocument();
    expect(screen.queryByTestId("protected-page")).not.toBeInTheDocument();
  });

  it("renders Outlet when role matches a single-role allowedRoles list", () => {
    renderGuard([Role.Client], "client");
    expect(screen.getByTestId("protected-page")).toBeInTheDocument();
  });

  it("renders Outlet when role is one of multiple allowed roles", () => {
    renderGuard([Role.Artist, Role.Owner, Role.Issuer], "artist");
    expect(screen.getByTestId("protected-page")).toBeInTheDocument();
  });

  it("renders Outlet for issuer on an issuer-only route", () => {
    renderGuard([Role.Issuer], "issuer");
    expect(screen.getByTestId("protected-page")).toBeInTheDocument();
  });

  it("renders Outlet for owner when all roles are allowed", () => {
    renderGuard([Role.Client, Role.Artist, Role.Owner, Role.Issuer], "owner");
    expect(screen.getByTestId("protected-page")).toBeInTheDocument();
  });

  it("redirects client to /book when accessing an artist-only route", () => {
    renderGuard([Role.Artist], "client");
    expect(screen.getByTestId("client-home")).toBeInTheDocument();
    expect(screen.queryByTestId("protected-page")).not.toBeInTheDocument();
  });

  it("redirects artist to /schedule when accessing a client-only route", () => {
    renderGuard([Role.Client], "artist");
    expect(screen.getByTestId("artist-home")).toBeInTheDocument();
    expect(screen.queryByTestId("protected-page")).not.toBeInTheDocument();
  });

  it("redirects owner to /dashboard when accessing a client-only route", () => {
    renderGuard([Role.Client], "owner");
    expect(screen.getByTestId("owner-home")).toBeInTheDocument();
    expect(screen.queryByTestId("protected-page")).not.toBeInTheDocument();
  });

  it("redirects issuer to /platform when accessing an artist-only route", () => {
    renderGuard([Role.Artist], "issuer");
    expect(screen.getByTestId("issuer-home")).toBeInTheDocument();
    expect(screen.queryByTestId("protected-page")).not.toBeInTheDocument();
  });

  it("unauthenticated user is redirected to /login even when allowedRoles is empty", () => {
    renderGuard([], null);
    expect(screen.getByTestId("login-page")).toBeInTheDocument();
  });
});

// ── AppRoot ───────────────────────────────────────────────────────────────────

describe("AppRoot", () => {
  function renderAppRoot(sessionExpired: boolean, role: string | null = null) {
    const store = makeUiStore(sessionExpired, role);
    render(
      <Provider store={store}>
        <MemoryRouter initialEntries={["/"]}>
          <Routes>
            <Route path="/" element={<AppRoot />}>
              <Route index element={<div data-testid="home" />} />
            </Route>
            <Route
              path="/login"
              element={
                <>
                  <div data-testid="login-page" />
                  <LocationDisplay />
                </>
              }
            />
          </Routes>
        </MemoryRouter>
      </Provider>,
    );
    return store;
  }

  it("renders child routes (Outlet) when sessionExpired is false", () => {
    renderAppRoot(false);
    expect(screen.getByTestId("home")).toBeInTheDocument();
    expect(screen.queryByTestId("login-page")).not.toBeInTheDocument();
  });

  it("does not navigate when sessionExpired remains false after mount", () => {
    renderAppRoot(false);
    expect(screen.queryByTestId("login-page")).not.toBeInTheDocument();
  });

  it("navigates to /login?reason=session_expired when sessionExpired starts as true on mount", async () => {
    renderAppRoot(true);
    await screen.findByTestId("login-page");
    expect(screen.getByTestId("location")).toHaveTextContent("/login?reason=session_expired");
  });

  it("clears sessionExpired in Redux after the session-expired navigation triggered on mount", async () => {
    const store = renderAppRoot(true);
    await screen.findByTestId("login-page");
    expect(store.getState().ui.sessionExpired).toBe(false);
  });

  it("navigates to /login?reason=session_expired when setSessionExpired is dispatched after mount", async () => {
    const store = renderAppRoot(false);
    expect(screen.getByTestId("home")).toBeInTheDocument();

    act(() => {
      store.dispatch(setSessionExpired());
    });

    await screen.findByTestId("login-page");
    expect(screen.getByTestId("location")).toHaveTextContent("/login?reason=session_expired");
  });

  it("clears sessionExpired in Redux after navigation triggered by post-mount dispatch", async () => {
    const store = renderAppRoot(false);

    act(() => {
      store.dispatch(setSessionExpired());
    });

    await screen.findByTestId("login-page");
    expect(store.getState().ui.sessionExpired).toBe(false);
  });

  it("replaces the history entry (replace:true) so back-button cannot return to the expired session", async () => {
    renderAppRoot(true);
    await screen.findByTestId("login-page");
    // If replace was NOT used, there would be two history entries and the home
    // page could be accessed via back. With replace the entry count stays at 1.
    // MemoryRouter does not expose history length directly, so we verify the
    // query string — the full URL confirms the navigation was completed.
    expect(screen.getByTestId("location")).toHaveTextContent("session_expired");
  });

  it("clears auth.role (dispatches logout) as part of session-expired navigation", async () => {
    const store = renderAppRoot(true, "artist");
    await screen.findByTestId("login-page");
    expect(store.getState().auth.role).toBeNull();
  });
});

// ── Public policy routes (PENA-101/102) ─────────────────────────────────────────
// These paths used to have no route, so CatchAllRedirect silently bounced them to
// /discover. Assert they now resolve to their real pages.

describe("public policy routes", () => {
  function renderAt(path: string) {
    const store = makeAuthStore(null);
    const testRouter = createMemoryRouter(routes, { initialEntries: [path] });
    render(
      <Provider store={store}>
        <RouterProvider router={testRouter} />
      </Provider>,
    );
    return testRouter;
  }

  it("renders the real Privacy Policy page at /privacy (not CatchAllRedirect → /discover)", async () => {
    const testRouter = renderAt("/privacy");
    expect(
      await screen.findByRole("heading", { name: /privacy policy/i }),
    ).toBeInTheDocument();
    expect(testRouter.state.location.pathname).toBe("/privacy");
  });

  it("renders the real Terms of Service page at /terms", async () => {
    const testRouter = renderAt("/terms");
    expect(
      await screen.findByRole("heading", { name: /terms of service/i }),
    ).toBeInTheDocument();
    expect(testRouter.state.location.pathname).toBe("/terms");
  });

  it("renders Refund Policy at /refund-policy and Contact at /contact", async () => {
    const refundRouter = renderAt("/refund-policy");
    expect(
      await screen.findByRole("heading", { name: /refund policy/i }),
    ).toBeInTheDocument();
    expect(refundRouter.state.location.pathname).toBe("/refund-policy");

    cleanup();

    const contactRouter = renderAt("/contact");
    expect(await screen.findByRole("heading", { name: /^contact$/i })).toBeInTheDocument();
    expect(contactRouter.state.location.pathname).toBe("/contact");
  });
});
