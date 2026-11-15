import { describe, it, expect, beforeAll, afterAll, afterEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import { PackageListPage } from "@/features/session-packages/components/PackageListPage";
import { packagesApi } from "@/features/session-packages/packagesApi";
import { studiosApi } from "@/features/studios/studiosApi";
import authReducer from "@/features/auth/authSlice";
import type { PackageResponse } from "@/features/session-packages/packages.types";

const PKG: PackageResponse = {
  id: "pkg-1", studioId: "s-1", name: "Sleeve series", sessionCount: 5, price: 500, isActive: true,
  createdAt: "2026-11-01T00:00:00Z",
};

let lastPut: { id: string; body: unknown } | null = null;

const server = setupServer(
  http.get("http://localhost/api/v1/packages", () => HttpResponse.json([PKG])),
  http.put("http://localhost/api/v1/packages/:id", async ({ params, request }) => {
    lastPut = { id: params.id as string, body: await request.json() };
    return HttpResponse.json({ ...PKG, isActive: false });
  }),
  http.get("http://localhost/api/v1/studios/me", () => HttpResponse.json({ id: "s-1", currency: "EUR", countryCode: "PT" })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "bypass" }));
afterEach(() => { server.resetHandlers(); lastPut = null; });
afterAll(() => server.close());

function renderPage() {
  const store = configureStore({
    reducer: {
      auth: authReducer,
      [packagesApi.reducerPath]: packagesApi.reducer,
      [studiosApi.reducerPath]:  studiosApi.reducer,
    },
    middleware: (gdm) => gdm().concat(packagesApi.middleware, studiosApi.middleware),
    preloadedState: {
      auth: {
        user: { id: "u-1", email: "owner@test.test" }, token: "fake-token", tenantId: "s-1", role: "owner",
        pendingReferralCode: null, impersonation: null,
      } as unknown as ReturnType<typeof authReducer>,
    },
  });
  render(<Provider store={store}><PackageListPage /></Provider>);
}

describe("PackageListPage", () => {
  it("the Active status is a real button the keyboard reaches, and Enter toggles it", async () => {
    const user = userEvent.setup();
    renderPage();
    const toggle = await screen.findByRole("button", { name: "Sleeve series is Active. Deactivate" });
    expect(toggle.tagName).toBe("BUTTON");
    toggle.focus();
    await user.keyboard("{Enter}");
    await waitFor(() => expect(lastPut?.id).toBe("pkg-1"));
    expect(lastPut?.body).toMatchObject({ isActive: false });
  });
});
