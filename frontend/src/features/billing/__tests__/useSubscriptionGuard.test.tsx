import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import type { ReactNode } from "react";
import authReducer from "@/features/auth/authSlice";
import { billingApi } from "../billingApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { useSubscriptionGuard } from "../useSubscriptionGuard";
import { Role } from "@/shared/types/roles";

let subscriptionStatus = "Active";
let studioActive = true;
const server = setupServer(
  http.get("http://localhost/api/v1/billing/subscription", () => HttpResponse.json({ status: subscriptionStatus })),
  http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json({ id: "s1", isActive: studioActive })),
);
beforeAll(() => server.listen({ onUnhandledRequest: "bypass" }));
afterEach(() => { server.resetHandlers(); subscriptionStatus = "Active"; studioActive = true; });
afterAll(() => server.close());

const SESSION = { sessionId: "sess-1", studioId: "s1", studioName: "Ink", expiresAt: "2030-01-01T00:00:00Z", reason: null };

function renderGuard(role: Role, supportSession: boolean) {
  const store = configureStore({
    reducer: { auth: authReducer, [billingApi.reducerPath]: billingApi.reducer, [studiosApi.reducerPath]: studiosApi.reducer },
    middleware: (gd) => gd().concat(billingApi.middleware, studiosApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "o@x.com" }, token: "t", tenantId: "s1", role, impersonation: supportSession ? SESSION : null } as any,
    },
  });
  const wrapper = ({ children }: { children: ReactNode }) => <Provider store={store}>{children}</Provider>;
  return renderHook(() => useSubscriptionGuard(), { wrapper });
}

describe("useSubscriptionGuard: the support-session cause (D05 phase 4b)", () => {
  it("a support session is read-only for the reason 'support', whatever the studio's state", () => {
    const { result } = renderGuard(Role.Owner, true);
    expect(result.current).toMatchObject({ isReadOnly: true, cause: "support" });
  });

  it("support takes precedence over a suspension: the server refuses the write for the session first", async () => {
    studioActive = false;
    const { result } = renderGuard(Role.Owner, true);
    await waitFor(() => expect(result.current.isSuspended).toBe(true));
    expect(result.current.cause).toBe("support");
  });

  it("support covers non-owner roles too", () => {
    const { result } = renderGuard(Role.Artist, true);
    expect(result.current).toMatchObject({ isReadOnly: true, cause: "support" });
  });

  it("outside a session an active owner can write", async () => {
    const { result } = renderGuard(Role.Owner, false);
    await waitFor(() => expect(result.current.status).toBe("Active"));
    expect(result.current).toMatchObject({ isReadOnly: false, cause: null });
  });
});
